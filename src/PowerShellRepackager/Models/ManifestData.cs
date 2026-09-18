namespace PowerShellRepackager.Models;

/// <summary>
/// Represents parsed metadata from a PowerShell module manifest (.psd1) file.
/// This is populated by parsing the manifest AST and used to understand the module structure
/// before rewriting it for repackaging.
/// </summary>
public class ManifestData
{
    /// <summary>
    /// The module name from the manifest's ModuleVersion or inferred from filename.
    /// </summary>
    public required string ModuleName { get; set; }

    /// <summary>
    /// The module version from the manifest (typically matches the .nupkg version).
    /// </summary>
    public required string ModuleVersion { get; set; }

    /// <summary>
    /// The RootModule property, if present in the manifest.
    /// This could be a .psm1 file, .dll file, or absence (script-only module).
    /// </summary>
    public string? RootModule { get; set; }

    /// <summary>
    /// The module GUID from the manifest (unique module identifier).
    /// When repackaging, this GUID should be regenerated to create a distinct module identity.
    /// </summary>
    public Guid? ModuleGuid { get; set; }

    /// <summary>
    /// Author of the module (for traceability).
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Company/organization associated with the module.
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// Description of the module.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Copyright statement of the original module.
    /// </summary>
    public string? Copyright { get; set; }

    /// <summary>
    /// PrivateData.PSData.ProjectUri of the original module.
    /// </summary>
    public string? ProjectUri { get; set; }

    /// <summary>
    /// PrivateData.PSData.LicenseUri of the original module. The original license governs redistribution.
    /// </summary>
    public string? LicenseUri { get; set; }

    /// <summary>
    /// PrivateData.PSData.IconUri of the original module.
    /// </summary>
    public string? IconUri { get; set; }

    /// <summary>
    /// HelpInfoURI of the original module (tied to the original module identity, so not carried over).
    /// </summary>
    public string? HelpInfoUri { get; set; }

    /// <summary>
    /// PrivateData.PSData.Tags of the original module.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// List of exported cmdlets, functions, aliases, etc.
    /// Used to preserve public API surface during repackaging.
    /// </summary>
    public string[]? ExportedCmdlets { get; set; }

    /// <summary>
    /// List of exported functions.
    /// </summary>
    public string[]? ExportedFunctions { get; set; }

    /// <summary>
    /// List of required modules/dependencies.
    /// Preserved for traceability and potential validation.
    /// </summary>
    public string[]? RequiredModules { get; set; }

    /// <summary>
    /// List of required assemblies (.NET DLLs) that must be loaded before the module.
    /// Often used for preload optimization in the generic loader.
    /// </summary>
    public string[]? RequiredAssemblies { get; set; }

    /// <summary>
    /// List of script files to process/import before the module.
    /// </summary>
    public string[]? ScriptsToProcess { get; set; }

    /// <summary>
    /// List of format definition files (.ps1xml).
    /// These should be preserved in the repackaged module.
    /// </summary>
    public string[]? FormatsToProcess { get; set; }

    /// <summary>
    /// The minimum PowerShell version required by this module.
    /// For repackaging, we assume PowerShell 7.4+.
    /// </summary>
    public string? PowerShellVersion { get; set; }

    /// <summary>
    /// The minimum PowerShell Core version, if specified separately.
    /// </summary>
    public string? PowerShellCoreVersion { get; set; }

    /// <summary>
    /// Indicates whether this is a binary module (has .dll root module).
    /// Affects how the repackaged module should be structured.
    /// </summary>
    public bool IsBinaryModule { get; set; }

    /// <summary>
    /// Raw manifest content or path (for debugging/reference).
    /// </summary>
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Raw manifest hash table representation (for advanced scenarios).
    /// </summary>
    public Dictionary<string, object>? ManifestHashtable { get; set; }
}
