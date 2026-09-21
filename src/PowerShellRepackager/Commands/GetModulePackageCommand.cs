using System.Management.Automation;
using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
namespace PowerShellRepackager.Commands;

/// <summary>
/// <para type="synopsis">Downloads a module package from the PowerShell Gallery</para>
/// <para type="description">Downloads a module package from the PowerShell Gallery and saves it to a temporary location.</para>
/// </summary>
/// <example>
/// <para type="name">Download a module package</para>
/// <para type="description">Download a module package from the PowerShell Gallery and save it to a temporary location.</para>
/// <code>Get-ModulePackage -ModuleName MyModule -Version 1.0.0</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsCommon.Get, "ModulePackage")]
[OutputType(typeof(string))]
public partial class GetModulePackageCommand : DependencyCmdlet<Startup>
{
    /// <summary>
    /// The name of the module to download.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNullOrEmpty]
    public string ModuleName { get; set; }

    /// <summary>
    /// The version of the module to download.
    /// </summary>
    [Parameter(Mandatory = false, Position = 1, ValueFromPipeline = true)]
    [ValidateNotNullOrEmpty]
    public string Version { get; set; }

    /// <summary>
    /// Force download the module package even if it already exists in the temporary location.
    /// </summary>
    [Parameter(Mandatory = false, Position = 2, ValueFromPipeline = true)]
    public SwitchParameter Force { get; set; }

    // Service dependencies are automatically injected by the DI framework when the cmdlet is instantiated.
    [ServiceDependency(Required = true)]
    private Mechanics.PackageDownloader _packageDownloader;

    [ServiceDependency(Required = true)]
    private ILogger<GetModulePackageCommand> _logger;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            var filePath = await _packageDownloader.DownloadPackageAsync(ModuleName, Version, Force.IsPresent, cancellationToken);
            WriteObject(filePath);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Failed to get module package, status code: {StatusCode}", ex.StatusCode);
            WriteError(new ErrorRecord(ex, "GetModulePackageFailed", ErrorCategory.InvalidOperation, null));
            return;
        }
    }
}

