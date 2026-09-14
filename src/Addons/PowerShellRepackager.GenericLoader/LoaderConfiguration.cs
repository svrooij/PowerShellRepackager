using System.Text.Json.Serialization;

namespace Svrooij.PowerShellRepackager.GenericLoader;

/// <summary>
/// Configuration for a repackaged module's isolated AssemblyLoadContext.
/// This file is generated per module and read by <see cref="GenericModuleInitializer"/> at import time.
/// </summary>
/// <remarks>
/// The loader automatically loads all assemblies found in the bin/{TargetFramework} folder,
/// so there is no need for an explicit allow-list. Only the target framework and critical
/// preload assemblies need to be specified.
/// </remarks>
public sealed class LoaderConfiguration
{
    /// <summary>
    /// The repackaged module name (e.g., "Svrooij.MicrosoftTeams").
    /// Used to create a recognizable ALC name for diagnostics.
    /// </summary>
    [JsonPropertyName("moduleName")]
    public required string ModuleName { get; set; }

    /// <summary>
    /// The target .NET framework folder name where bundled DLLs are located (e.g., "net8.0", "netcore3.1").
    /// The loader discovers and loads all .dll files from the module's bin/{TargetFramework} folder.
    /// </summary>
    [JsonPropertyName("targetFramework")]
    public required string TargetFramework { get; set; }

    /// <summary>
    /// List of assembly simple names (without .dll extension) to preload eagerly when the module is imported.
    /// These are loaded immediately to ensure they are available before any static constructors
    /// or module initialization code runs that might expect them.
    /// All other assemblies in the folder are loaded lazily on first request.
    /// </summary>
    [JsonPropertyName("preloadAssemblies")]
    public required string[] PreloadAssemblies { get; set; }

    /// <summary>
    /// Optional: The original module name before repackaging (e.g., "MicrosoftTeams").
    /// Used for traceability and audit logs.
    /// </summary>
    [JsonPropertyName("originalModuleName")]
    public string? OriginalModuleName { get; set; }

    /// <summary>
    /// Optional: The original module version before repackaging (e.g., "7.9.0").
    /// Used for traceability and audit logs.
    /// </summary>
    [JsonPropertyName("originalModuleVersion")]
    public string? OriginalModuleVersion { get; set; }

    /// <summary>
    /// Optional: Timestamp when this module was repackaged.
    /// </summary>
    [JsonPropertyName("repackageDate")]
    public string? RepackageDate { get; set; }

    /// <summary>
    /// Optional: Version of the PowerShellRepackager tool used to create this configuration.
    /// </summary>
    [JsonPropertyName("toolVersion")]
    public string? ToolVersion { get; set; }
}
