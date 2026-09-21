namespace PowerShellRepackager.Models;

/// <summary>
/// Represents detailed information about an assembly file, including version metadata.
/// </summary>
public class AssemblyFileInfo
{
    /// <summary>
    /// The relative path of the assembly file within the extracted package.
    /// </summary>
    public required string RelativePath { get; set; }

    /// <summary>
    /// The assembly version extracted from the file metadata.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// The assembly file version extracted from the file metadata.
    /// </summary>
    public string? FileVersion { get; set; }

    /// <summary>
    /// The assembly product version extracted from the file metadata.
    /// </summary>
    public string? ProductVersion { get; set; }

    /// <summary>
    /// Assembly name extracted from the file metadata.
    /// </summary>
    public string? AssemblyName { get; set; }

    /// <summary>
    /// Error message if version extraction failed (e.g., corrupted file).
    /// </summary>
    public string? Error { get; set; }
}
