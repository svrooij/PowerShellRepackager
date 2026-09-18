using System.Text.Json.Serialization;

namespace Svrooij.PowerShellRepackager.GenericLoader;

/// <summary>
/// Configuration for a repackaged module's isolated AssemblyLoadContext.
/// This file is generated per module and read by <see cref="GenericModuleInitializer"/> at import time.
/// </summary>
/// <remarks>
/// The loader searches assemblies in the paths specified by assemblySearchPaths.
/// This allows modules to preserve their original folder structure (framework-specific subfolders, etc.)
/// while still maintaining dependency isolation through the AssemblyLoadContext.
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
    /// List of assembly simple names (without .dll extension) to preload eagerly when the module is imported.
    /// These are loaded immediately to ensure they are available before any static constructors
    /// or module initialization code runs that might expect them.
    /// All other assemblies in the search paths are loaded lazily on first request.
    /// </summary>
    [JsonPropertyName("preloadAssemblies")]
    public required string[] PreloadAssemblies { get; set; }

    /// <summary>
    /// Relative paths within the module where assemblies can be found.
    /// Examples: ["bin"], ["bin", "net48", "netcore3.1"]
    /// The loader searches these paths in order when resolving assembly dependencies.
    /// If not specified, defaults to ["bin"] for backward compatibility.
    /// </summary>
    [JsonPropertyName("assemblySearchPaths")]
    public string[]? AssemblySearchPaths { get; set; }

    /// <summary>
    /// Relative paths to NuGet-style <c>runtimes</c> folders (e.g. ["netCore/runtimes"]).
    /// At import time the loader selects the <c>{rid}/native</c> and <c>{rid}/lib/*</c> subfolders that match
    /// the current OS and process architecture (e.g. win-x64), so only the correct native binaries are loaded.
    /// </summary>
    [JsonPropertyName("runtimesPaths")]
    public string[]? RuntimesPaths { get; set; }

    /// <summary>
    /// Simple names (without .dll) of the assemblies that are loaded into the private AssemblyLoadContext.
    /// These are the module's <em>dependencies</em> (e.g. Microsoft.Identity.Client, Newtonsoft.Json).
    /// Assemblies found in the search paths that are not listed here are considered module-owned and are
    /// loaded into the Default ALC instead, so PowerShell can resolve their types in scripts
    /// (type literals, parameter constraints, format/type files).
    /// If null or empty, every assembly in the search paths is isolated (legacy behavior).
    /// </summary>
    [JsonPropertyName("isolatedAssemblies")]
    public string[]? IsolatedAssemblies { get; set; }

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
