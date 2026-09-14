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

    [ServiceDependency(Required = true)]
    private ILogger<GetModulePackageCommand> _logger;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        var fileName = $"{ModuleName}.{Version}.nupkg";
        var filePath = Path.Combine(Path.GetTempPath(), "PowerShellRepackager", fileName);
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!Directory.Exists(directoryPath!))
        {
            Directory.CreateDirectory(directoryPath!);
        }
        if (File.Exists(filePath))
        {
            if (!Force.IsPresent)
            {
                _logger.LogInformation("Module package {FilePath} already exists. Skipping download.", filePath);
                WriteObject(filePath);
                return;
            }
            else
            {
                _logger.LogInformation("Module package {FilePath} already exists. Overwriting due to -Force parameter.", filePath);
            }
        }
        using var httpclient = new HttpClient();
        var url = $"https://www.powershellgallery.com/api/v2/package/{ModuleName}/{Version}";
        var response = await httpclient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to get module package from {Url}. Status code: {StatusCode}", url, response.StatusCode);
            WriteError(new ErrorRecord(new Exception($"Failed to get module package from {url}. Status code: {response.StatusCode}"), "GetModulePackageFailed", ErrorCategory.InvalidOperation, null));
            return;
        }
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, cancellationToken);
        WriteObject(filePath);
    }
}
