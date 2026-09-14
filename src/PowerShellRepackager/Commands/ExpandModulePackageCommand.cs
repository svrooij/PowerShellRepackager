using System.Management.Automation;
using Microsoft.Extensions.Logging;
using PowerShellRepackager.Models;
using Svrooij.PowerShell.DI;

namespace PowerShellRepackager.Commands;

/// <summary>
/// <para type="synopsis">Extracts and analyzes a module package</para>
/// <para type="description">
/// Extracts a .nupkg module package to a working directory and analyzes its contents.
/// Returns information about the module structure, detected .NET target frameworks 
/// (including both netcore3.1 and netcoreapp3.1 conventions), and categorized files 
/// (manifest, assemblies, scripts, data).
/// </para>
/// </summary>
/// <example>
/// <para type="name">Extract and analyze a module package</para>
/// <para type="description">Extract a downloaded module package and get structured information about its contents.</para>
/// <code>Expand-ModulePackage -PackagePath "C:\temp\MicrosoftTeams.7.9.0.nupkg"</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsData.Expand, "ModulePackage")]
[OutputType(typeof(ModulePackageInfo))]
public partial class ExpandModulePackageCommand : DependencyCmdlet<Startup>
{
    /// <summary>
    /// The full path to the .nupkg package file to extract.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNullOrEmpty]
    public string PackagePath { get; set; }

    /// <summary>
    /// The name of the module (used to organize the extraction working directory).
    /// If not provided, will be inferred from the package filename.
    /// </summary>
    [Parameter(Mandatory = false, Position = 1, ValueFromPipelineByPropertyName = true)]
    public string? ModuleName { get; set; }

    /// <summary>
    /// The version of the module (used to organize the extraction working directory).
    /// If not provided, will be inferred from the package filename.
    /// </summary>
    [Parameter(Mandatory = false, Position = 2, ValueFromPipelineByPropertyName = true)]
    public string? Version { get; set; }

    [ServiceDependency(Required = true)]
    private Mechanics.PackageExtractor _packageExtractor;

    [ServiceDependency(Required = true)]
    private ILogger<ExpandModulePackageCommand> _logger;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Infer module name and version from package filename if not provided
            var moduleName = ModuleName ?? InferModuleNameFromPath(PackagePath);
            var version = Version ?? InferVersionFromPath(PackagePath);

            if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(version))
            {
                var ex = new ArgumentException(
                    "Could not infer module name or version from package path. Please provide -ModuleName and -Version explicitly.");
                WriteError(new ErrorRecord(ex, "ExpandModulePackageFailed", ErrorCategory.InvalidArgument, PackagePath));
                return;
            }

            _logger.LogInformation("Expanding module package: {ModuleName} v{Version}", moduleName, version);

            var packageInfo = await _packageExtractor.ExtractPackageAsync(PackagePath, moduleName, version, cancellationToken);
            WriteObject(packageInfo);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError("Package file not found: {Path}", PackagePath);
            WriteError(new ErrorRecord(ex, "ExpandModulePackageFailed", ErrorCategory.ObjectNotFound, PackagePath));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("Failed to extract package: {Message}", ex.Message);
            WriteError(new ErrorRecord(ex, "ExpandModulePackageFailed", ErrorCategory.InvalidOperation, PackagePath));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Package extraction cancelled");
            WriteError(new ErrorRecord(
                new OperationCanceledException("Operation cancelled by user"),
                "OperationCancelled",
                ErrorCategory.OperationStopped,
                PackagePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error extracting package");
            WriteError(new ErrorRecord(ex, "ExpandModulePackageFailed", ErrorCategory.NotSpecified, PackagePath));
        }
    }

    /// <summary>
    /// Infers the module name from a .nupkg filename (format: {Name}.{Version}.nupkg).
    /// </summary>
    private static string? InferModuleNameFromPath(string packagePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(packagePath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        // Handle format: Name.Version.nupkg → Name
        // Common pattern: MicrosoftTeams.7.9.0 → MicrosoftTeams
        var parts = fileName.Split('.');
        if (parts.Length < 2)
            return fileName;

        // Find where version starts (first part that looks like a number)
        var lastNamePartIndex = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (char.IsDigit(parts[i][0]))
                break;
            lastNamePartIndex = i;
        }

        var name = string.Join(".", parts, 0, lastNamePartIndex + 1);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Infers the version from a .nupkg filename (format: {Name}.{Version}.nupkg).
    /// </summary>
    private static string? InferVersionFromPath(string packagePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(packagePath);
        if (string.IsNullOrEmpty(fileName))
            return null;

        // Handle format: Name.Version.nupkg → Version
        // Common pattern: MicrosoftTeams.7.9.0 → 7.9.0
        var parts = fileName.Split('.');
        if (parts.Length < 2)
            return null;

        // Find where version starts and join remaining parts
        var foundStart = false;
        var versionParts = new List<string>();
        foreach (var part in parts)
        {
            if (!foundStart && char.IsDigit(part[0]))
            {
                foundStart = true;
            }

            if (foundStart)
            {
                versionParts.Add(part);
            }
        }

        return versionParts.Count > 0 ? string.Join(".", versionParts) : null;
    }
}
