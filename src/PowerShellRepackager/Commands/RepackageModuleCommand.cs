using System.Management.Automation;
using Microsoft.Extensions.Logging;
using PowerShellRepackager.Models;
using Svrooij.PowerShell.DI;

namespace PowerShellRepackager.Commands;

/// <summary>
/// <para type="synopsis">Repackages an extracted module with a new module loader</para>
/// <para type="description">
/// Takes extracted module contents and repackages them into a new module structure that uses the
/// generic assembly loader. This command:
/// - Parses the original manifest to understand module structure
/// - Consolidates all assemblies into a flat /bin directory
/// - Generates a new module GUID (making it a distinct module)
/// - Rewrites the manifest with the new module name and generic loader entry point
/// - Generates the loader configuration file
/// - Copies the prebuilt generic loader DLL
/// 
/// Output is placed in: {OutputPath}/{OriginalName}-{OriginalVersion}/{NewModuleName}/
/// If OutputPath is not specified, uses the current directory.
/// 
/// With -Pack a .nupkg is created next to the module folder; with -Publish that package is also
/// pushed to a NuGet v2 feed (default: PowerShell Gallery) using -ApiKey or the PSGALLERY_API_KEY /
/// NUGET_API_KEY environment variable.
/// </para>
/// </summary>
/// <example>
/// <para type="name">Repackage an extracted module</para>
/// <para type="description">
/// First extract a module, then repackage it with a new name and output location.
/// </para>
/// <code>
/// $extracted = Expand-ModulePackage -PackagePath "C:\temp\MicrosoftTeams.7.9.0.nupkg"
/// $repackaged = Publish-RepackagedModule -ModuleInfo $extracted -NewModuleName "Svrooij.MicrosoftTeams" -OutputPath "C:\output"
/// </code>
/// </example>
/// <example>
/// <para type="name">Repackage and publish to the PowerShell Gallery</para>
/// <para type="description">
/// Uses the API key from the PSGALLERY_API_KEY environment variable.
/// </para>
/// <code>
/// Publish-RepackagedModule -ModuleInfo $extracted -NewModuleName "Svrooij.MicrosoftTeams" -OutputPath "C:\output" -Publish
/// </code>
/// </example>
/// <example>
/// <para type="name">Repackage and publish to a private feed</para>
/// <code>
/// Publish-RepackagedModule -ModuleInfo $extracted -NewModuleName "Svrooij.MicrosoftTeams" -Publish -FeedUrl "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v2" -ApiKey $pat
/// </code>
/// </example>
/// <example>
/// <para type="name">Override assembly isolation</para>
/// <para type="description">
/// Force all Microsoft.Graph.* assemblies into the private load context and keep a contract assembly shared
/// with PowerShell so scripts can reference its types.
/// </para>
/// <code>
/// Publish-RepackagedModule -ModuleInfo $extracted -NewModuleName "Svrooij.Contoso" -IsolateAssembly 'Microsoft.Graph.*' -SharedAssembly 'Contoso.Contracts'
/// </code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsData.Publish, "RepackagedModule")]
[OutputType(typeof(ModuleRepackageInfo))]
public partial class RepackageModuleCommand : DependencyCmdlet<Startup>
{
    /// <summary>
    /// The extracted module information from Expand-ModulePackage.
    /// Contains paths to all extracted files and metadata.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModulePackageInfo ModuleInfo { get; set; }

    /// <summary>
    /// The name for the repackaged module.
    /// Example: "Svrooij.MicrosoftTeams"
    /// </summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromPipelineByPropertyName = true)]
    [ValidateNotNullOrEmpty]
    public string NewModuleName { get; set; }

    /// <summary>
    /// The output base directory where the repackaged module will be created.
    /// Final structure: {OutputPath}/{OriginalName}-{OriginalVersion}/{NewModuleName}/
    /// If not specified, uses the current directory.
    /// </summary>
    [Parameter(Mandatory = false, Position = 2, ValueFromPipelineByPropertyName = true)]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Create a .nupkg of the repackaged module next to the output folder, without publishing it.
    /// Implied by -Publish.
    /// </summary>
    [Parameter(Mandatory = false)]
    public SwitchParameter Pack { get; set; }

    /// <summary>
    /// Pack the repackaged module and publish it to the feed specified by -FeedUrl.
    /// </summary>
    [Parameter(Mandatory = false)]
    public SwitchParameter Publish { get; set; }

    /// <summary>
    /// NuGet v2 feed to publish to. Defaults to the PowerShell Gallery (https://www.powershellgallery.com/api/v2).
    /// </summary>
    [Parameter(Mandatory = false, ValueFromPipelineByPropertyName = true)]
    [ValidateNotNullOrEmpty]
    public string FeedUrl { get; set; } = Mechanics.ModulePublisher.DefaultFeedUrl;

    /// <summary>
    /// API key for the feed. When omitted, the PSGALLERY_API_KEY or NUGET_API_KEY environment variable is used.
    /// </summary>
    [Parameter(Mandatory = false)]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Assembly names (without .dll) that must always be isolated in the module's private AssemblyLoadContext,
    /// overriding the automatic classification. Supports wildcards, e.g. 'Microsoft.Graph.*', 'Azure.Core'.
    /// </summary>
    [Parameter(Mandatory = false)]
    [SupportsWildcards]
    public string[]? IsolateAssembly { get; set; }

    /// <summary>
    /// Assembly names (without .dll) that must be loaded into the Default AssemblyLoadContext (shared with
    /// PowerShell), overriding the automatic classification. Use this when scripts reference types from an
    /// assembly that was isolated by mistake. Supports wildcards, e.g. 'Contoso.Contracts*'.
    /// </summary>
    [Parameter(Mandatory = false)]
    [SupportsWildcards]
    public string[]? SharedAssembly { get; set; }

    [ServiceDependency(Required = true)]
    private Mechanics.ModuleRepackager _moduleRepackager;

    [ServiceDependency(Required = true)]
    private Mechanics.ModulePublisher _modulePublisher;

    [ServiceDependency(Required = true)]
    private ILogger<RepackageModuleCommand> _logger;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (ModuleInfo == null)
            {
                var ex = new ArgumentNullException(nameof(ModuleInfo),
                    "ModuleInfo is required. Pipe from Expand-ModulePackage or provide manually.");
                WriteError(new ErrorRecord(ex, "RepackageModuleFailed", ErrorCategory.InvalidArgument, null));
                return;
            }

            _logger.LogInformation(
                "Repackaging module: {Module} v{Version} → {NewName}",
                ModuleInfo.ModuleName,
                ModuleInfo.Version,
                NewModuleName);

            var repackageInfo = await _moduleRepackager.RepackageModuleAsync(
                ModuleInfo,
                NewModuleName,
                OutputPath,
                IsolateAssembly,
                SharedAssembly,
                cancellationToken);

            _logger.LogInformation("Repackaging successful: {Summary}", repackageInfo.GetSummary());

            if (Publish.IsPresent || Pack.IsPresent)
            {
                string? apiKey = null;
                if (Publish.IsPresent)
                {
                    apiKey = Mechanics.ModulePublisher.ResolveApiKey(ApiKey);
                    if (apiKey == null)
                    {
                        var ex = new ArgumentException(
                            "No API key provided. Pass -ApiKey or set the " +
                            string.Join(" or ", Mechanics.ModulePublisher.ApiKeyEnvironmentVariables) + " environment variable.");
                        WriteError(new ErrorRecord(ex, "PublishApiKeyMissing", ErrorCategory.InvalidArgument, null));
                        WriteObject(repackageInfo);
                        return;
                    }
                }

                repackageInfo.PackagePath = await _modulePublisher.CreatePackageAsync(repackageInfo, cancellationToken);

                if (Publish.IsPresent)
                {
                    await _modulePublisher.PublishAsync(repackageInfo.PackagePath, FeedUrl, apiKey!, cancellationToken);
                    repackageInfo.PublishedToFeed = FeedUrl;
                }
            }

            WriteObject(repackageInfo);
        }
        catch (UnauthorizedAccessException ex) when (Publish.IsPresent)
        {
            _logger.LogError(ex, "Feed rejected the API key");
            WriteError(new ErrorRecord(ex, "PublishUnauthorized", ErrorCategory.AuthenticationError, FeedUrl));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Publishing failed: {Message}", ex.Message);
            WriteError(new ErrorRecord(ex, "PublishFailed", ErrorCategory.ConnectionError, FeedUrl));
        }
        catch (ArgumentNullException ex)
        {
            _logger.LogError(ex, "Missing required parameter");
            WriteError(new ErrorRecord(ex, "RepackageModuleFailed", ErrorCategory.InvalidArgument, ModuleInfo));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to repackage module: {Message}", ex.Message);
            WriteError(new ErrorRecord(ex, "RepackageModuleFailed", ErrorCategory.InvalidOperation, ModuleInfo));
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "Output directory not found or inaccessible");
            WriteError(new ErrorRecord(ex, "RepackageModuleFailed", ErrorCategory.WriteError, OutputPath));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied to output directory");
            WriteError(new ErrorRecord(ex, "RepackageModuleFailed", ErrorCategory.PermissionDenied, OutputPath));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Module repackaging cancelled by user");
            WriteError(new ErrorRecord(
                new OperationCanceledException("Operation cancelled by user"),
                "OperationCancelled",
                ErrorCategory.OperationStopped,
                ModuleInfo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during module repackaging");
            WriteError(new ErrorRecord(ex, "RepackageModuleFailed", ErrorCategory.NotSpecified, ModuleInfo));
        }
    }
}
