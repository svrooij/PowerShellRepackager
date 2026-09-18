namespace PowerShellRepackager.Models;

/// <summary>
/// Represents the extracted and normalized content of a module package (.nupkg).
/// This model is populated by <see cref="Mechanics.PackageExtractor"/> and serves as the input
/// to all subsequent repackaging steps.
/// </summary>
public class ModulePackageInfo
{
    /// <summary>
    /// The original module name (e.g., "MicrosoftTeams").
    /// </summary>
    public required string ModuleName { get; set; }

    /// <summary>
    /// The module version (e.g., "7.9.0").
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// The working directory where this package was extracted.
    /// All file paths in <see cref="Files"/> are relative to this directory.
    /// </summary>
    public required string ExtractedPath { get; set; }

    /// <summary>
    /// The detected target framework for assemblies (e.g., Net80, NetCore31).
    /// Repackaging will use only this single TFM; others are discarded.
    /// </summary>
    public required TargetFramework SelectedTargetFramework { get; set; }

    /// <summary>
    /// All files discovered in the extracted package, categorized by type.
    /// </summary>
    public required IReadOnlyList<ModuleFile> Files { get; set; }

    /// <summary>
    /// The manifest file (ModuleName.psd1).
    /// This is always present and required.
    /// </summary>
    public ModuleFile? ManifestFile => Files.FirstOrDefault(f => f.FileType == ModuleFileType.Manifest);

    /// <summary>
    /// The root module file if present (as specified in the manifest's RootModule property).
    /// May be null if the module has no explicit root module, or if manifest parsing hasn't been performed yet.
    /// Note: Currently not populated by the extractor (Step 2). Will be identified by manifest parsing in later steps.
    /// </summary>
    public ModuleFile? RootModuleFile => Files.FirstOrDefault(f => f.FileType == ModuleFileType.RootModule);

    /// <summary>
    /// All assembly (.dll) files found in the selected TFM folder.
    /// These are the files that will be isolated in the private ALC.
    /// </summary>
    public IReadOnlyList<ModuleFile> Assemblies => Files
        .Where(f => f.FileType == ModuleFileType.Assembly)
        .ToList();

    /// <summary>
    /// All script files (.ps1, .psm1, .psd1, etc.) found in the module.
    /// </summary>
    public IReadOnlyList<ModuleFile> Scripts => Files
        .Where(f => f.FileType == ModuleFileType.Script)
        .ToList();

    /// <summary>
    /// All data files (readme, license, examples, etc.) found in the module.
    /// </summary>
    public IReadOnlyList<ModuleFile> DataFiles => Files
        .Where(f => f.FileType == ModuleFileType.Data)
        .ToList();

    /// <summary>
    /// All other files that don't fit specific categories.
    /// </summary>
    public IReadOnlyList<ModuleFile> OtherFiles => Files
        .Where(f => f.FileType == ModuleFileType.Other)
        .ToList();
}
