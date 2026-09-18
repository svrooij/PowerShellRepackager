namespace PowerShellRepackager.Models;

/// <summary>
/// Represents the output of the repackaging process (Steps 3-5).
/// Contains information about the repackaged module structure and files.
/// </summary>
public class ModuleRepackageInfo
{
    /// <summary>
    /// The original module name (e.g., "MicrosoftTeams").
    /// </summary>
    public required string OriginalModuleName { get; set; }

    /// <summary>
    /// The original module version (e.g., "7.9.0").
    /// </summary>
    public required string OriginalModuleVersion { get; set; }

    /// <summary>
    /// The new repackaged module name (e.g., "Svrooij.MicrosoftTeams").
    /// </summary>
    public required string RepackagedModuleName { get; set; }

    /// <summary>
    /// The root directory where the repackaged module was created.
    /// Structure: {OutputPath}/{OriginalName}-{OriginalVersion}/{RepackagedModuleName}/
    /// </summary>
    public required string OutputDirectory { get; set; }

    /// <summary>
    /// The bin directory containing all consolidated DLLs.
    /// Path: {OutputDirectory}/bin/
    /// </summary>
    public required string BinDirectory { get; set; }

    /// <summary>
    /// Path to the rewritten manifest file (.psd1).
    /// </summary>
    public required string ManifestPath { get; set; }

    /// <summary>
    /// Path to the generated loader configuration file (loader-config.json).
    /// </summary>
    public required string LoaderConfigPath { get; set; }

    /// <summary>
    /// Path to the generic loader DLL that was copied to bin/.
    /// </summary>
    public required string LoaderDllPath { get; set; }

    /// <summary>
    /// Total number of assemblies (DLLs) consolidated into bin/.
    /// </summary>
    public int AssemblyCount { get; set; }

    /// <summary>
    /// Total number of files in the repackaged module.
    /// </summary>
    public int TotalFileCount { get; set; }

    /// <summary>
    /// The generated GUID for the repackaged module (unique identity).
    /// This is regenerated from the original GUID for uniqueness.
    /// </summary>
    public Guid RepackagedModuleGuid { get; set; }

    /// <summary>
    /// The selected target framework used for DLL consolidation (e.g., NetCore31).
    /// Preserved for traceability; loader doesn't need this.
    /// </summary>
    public TargetFramework SelectedTargetFramework { get; set; }

    /// <summary>
    /// Timestamp when repackaging was completed.
    /// </summary>
    public DateTime RepackageDate { get; set; }

    /// <summary>
    /// List of assemblies that were marked for preload in loader-config.json.
    /// </summary>
    public string[]? PreloadAssemblies { get; set; }

    /// <summary>
    /// List of all files included in the repackaged module.
    /// </summary>
    public string[]? IncludedFiles { get; set; }

    /// <summary>
    /// Optional: Version of the repackaging tool that created this module.
    /// </summary>
    public string? RepackagerVersion { get; set; }

    /// <summary>
    /// Path to the generated .nupkg, when packaging was requested (-Publish or -Pack).
    /// </summary>
    public string? PackagePath { get; set; }

    /// <summary>
    /// The feed the package was published to, when -Publish was specified.
    /// </summary>
    public string? PublishedToFeed { get; set; }

    /// <summary>
    /// Summary statistics about the repackaging operation.
    /// </summary>
    public string GetSummary() => $"Repackaged '{OriginalModuleName}' v{OriginalModuleVersion} → '{RepackagedModuleName}' " +
        $"({AssemblyCount} assemblies, {TotalFileCount} total files) to {OutputDirectory}";
}
