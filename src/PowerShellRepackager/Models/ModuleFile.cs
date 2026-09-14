namespace PowerShellRepackager.Models;

/// <summary>
/// Represents a file within an extracted module package.
/// </summary>
public record ModuleFile
{
    /// <summary>
    /// The relative path of the file within the extracted package (e.g., "MicrosoftTeams.psd1", "bin/net8.0/Microsoft.Identity.Client.dll").
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// The type/category of this file.
    /// </summary>
    public required ModuleFileType FileType { get; init; }

    /// <summary>
    /// The full file system path to this file on disk.
    /// </summary>
    public required string FullPath { get; init; }
}

/// <summary>
/// Categorizes different file types found in a module package.
/// </summary>
public enum ModuleFileType
{
    /// <summary>
    /// The module manifest (.psd1 file).
    /// </summary>
    Manifest,

    /// <summary>
    /// Root module script or binary (.psm1 or .dll, as specified in manifest's RootModule).
    /// </summary>
    RootModule,

    /// <summary>
    /// Bundled .NET assembly (.dll) in the TFM-specific bin folder.
    /// </summary>
    Assembly,

    /// <summary>
    /// PowerShell script file (.ps1, .ps1xml, etc.).
    /// </summary>
    Script,

    /// <summary>
    /// Data file or documentation (readme, license, examples, etc.).
    /// </summary>
    Data,

    /// <summary>
    /// Other files (to be preserved as-is during repackaging).
    /// </summary>
    Other,
}
