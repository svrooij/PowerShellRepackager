using System.Management.Automation;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Extensions.Logging;
using PowerShellRepackager.Models;
using Svrooij.PowerShell.DI;
using System.Collections.Immutable;

namespace PowerShellRepackager.Commands;

/// <summary>
/// <para type="synopsis">Describes an expanded module package</para>
/// <para type="description">
/// Provides detailed information about an expanded module package, including all files,
/// categorized by type (assemblies, scripts, data, etc.). For assemblies, it extracts
/// and displays version information from the file metadata without loading dependencies.
/// </para>
/// </summary>
/// <example>
/// <para type="name">Describe an expanded module package</para>
/// <para type="description">Get detailed description of an expanded package including assembly versions.</para>
/// <code>Expand-ModulePackage -PackagePath "C:\temp\MicrosoftTeams.7.9.0.nupkg" | Describe-ModulePackage</code>
/// </example>
[GenerateBindings]
[Cmdlet(VerbsCommon.Get, "ModulePackageDescription")]
[OutputType(typeof(ModulePackageDescription))]
public partial class DescribeModulePackageCommand : DependencyCmdlet<Startup>
{
    /// <summary>
    /// The expanded module package information from Expand-ModulePackage.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [ValidateNotNull]
    public ModulePackageInfo PackageInfo { get; set; }

    [ServiceDependency(Required = true)]
    private ILogger<DescribeModulePackageCommand> _logger;

    /// <inheritdoc />
    public override async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Describing module package: {ModuleName} v{Version}", PackageInfo.ModuleName, PackageInfo.Version);

            // Collect assembly information with version details
            var assemblyInfos = new List<AssemblyFileInfo>();
            foreach (var assembly in PackageInfo.Assemblies)
            {
                var assemblyInfo = ExtractAssemblyVersion(assembly);
                assemblyInfos.Add(assemblyInfo);
            }

            // Create the description
            var description = new ModulePackageDescription
            {
                ModuleName = PackageInfo.ModuleName,
                Version = PackageInfo.Version,
                ExtractedPath = PackageInfo.ExtractedPath,
                SelectedTargetFramework = PackageInfo.SelectedTargetFramework.ToString(),
                Assemblies = assemblyInfos.AsReadOnly(),
                Scripts = PackageInfo.Scripts.Select(f => f.RelativePath).ToList().AsReadOnly(),
                DataFiles = PackageInfo.DataFiles.Select(f => f.RelativePath).ToList().AsReadOnly(),
                OtherFiles = PackageInfo.OtherFiles.Select(f => f.RelativePath).ToList().AsReadOnly(),
                ManifestFile = PackageInfo.ManifestFile?.RelativePath
            };

            WriteObject(description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error describing module package");
            WriteError(new ErrorRecord(ex, "DescribeModulePackageFailed", ErrorCategory.NotSpecified, PackageInfo));
        }
    }

    /// <summary>
    /// Extracts version information from an assembly file using System.Reflection.Metadata.
    /// This avoids loading the assembly into the runtime, which prevents dependency issues.
    /// </summary>
    private AssemblyFileInfo ExtractAssemblyVersion(ModuleFile assemblyFile)
    {
        var info = new AssemblyFileInfo
        {
            RelativePath = assemblyFile.RelativePath
        };

        try
        {
            // First, try to get file version info from the file itself
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assemblyFile.FullPath);
            if (!string.IsNullOrEmpty(fileVersionInfo.FileVersion))
            {
                info.FileVersion = fileVersionInfo.FileVersion;
            }
            if (!string.IsNullOrEmpty(fileVersionInfo.ProductVersion))
            {
                info.ProductVersion = fileVersionInfo.ProductVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract file version info for {Assembly}", assemblyFile.RelativePath);
        }

        try
        {
            // Use System.Reflection.Metadata to read assembly metadata without loading
            using (var stream = File.OpenRead(assemblyFile.FullPath))
            using (var peReader = new PEReader(stream))
            {
                if (peReader.HasMetadata)
                {
                    var metadataReader = peReader.GetMetadataReader();

                    // Get assembly name and version
                    var assemblyDef = metadataReader.GetAssemblyDefinition();
                    var assemblyName = metadataReader.GetString(assemblyDef.Name);
                    info.AssemblyName = assemblyName;

                    // Get version from assembly definition
                    var version = assemblyDef.Version;
                    info.Version = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract version metadata from assembly {Assembly}: {Message}", assemblyFile.RelativePath, ex.Message);
            if (info.Error == null)
            {
                info.Error = ex.Message;
            }
        }

        return info;
    }
}
