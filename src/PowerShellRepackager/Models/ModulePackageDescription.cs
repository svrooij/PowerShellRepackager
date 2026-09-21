namespace PowerShellRepackager.Models;

/// <summary>
/// Represents a detailed description of an expanded module package, including file listings and assembly versions.
/// </summary>
public class ModulePackageDescription
{
    /// <summary>
    /// The module name.
    /// </summary>
    public required string ModuleName { get; set; }

    /// <summary>
    /// The module version.
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// The extracted path of the package.
    /// </summary>
    public required string ExtractedPath { get; set; }

    /// <summary>
    /// The selected target framework for the module.
    /// </summary>
    public required string SelectedTargetFramework { get; set; }

    /// <summary>
    /// All assemblies found in the package with version information.
    /// </summary>
    public required IReadOnlyList<AssemblyFileInfo> Assemblies { get; set; }

    /// <summary>
    /// All script files found in the package.
    /// </summary>
    public required IReadOnlyList<string> Scripts { get; set; }

    /// <summary>
    /// All data files found in the package.
    /// </summary>
    public required IReadOnlyList<string> DataFiles { get; set; }

    /// <summary>
    /// All other files found in the package.
    /// </summary>
    public required IReadOnlyList<string> OtherFiles { get; set; }

    /// <summary>
    /// The manifest file if present.
    /// </summary>
    public string? ManifestFile { get; set; }
}
