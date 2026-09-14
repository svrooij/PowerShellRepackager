using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Svrooij.PowerShellRepackager.GenericLoader;

/// <summary>
/// A generic, reusable AssemblyLoadContext that isolates a repackaged module's bundled assemblies.
/// </summary>
/// <remarks>
/// This ALC is configured at runtime via <see cref="LoaderConfiguration"/>.
/// All bundled assemblies are consolidated into a flat bin/ folder in the repackaged module.
/// The loader loads all DLLs from that single folder, allowing all other assembly requests
/// to fall through to the Default ALC.
/// This ensures that framework assemblies (System.*, System.Management.Automation) and
/// PowerShell SDK assemblies remain resolvable from the Default ALC, maintaining type compatibility.
/// </remarks>
internal sealed class GenericAssemblyLoadContext : AssemblyLoadContext
{
    private readonly LoaderConfiguration _config;
    private readonly string _binDirectory;

    /// <summary>
    /// Initializes a new instance of the generic ALC with the given configuration.
    /// </summary>
    /// <param name="config">The loader configuration for this module.</param>
    /// <param name="loaderAssemblyLocation">
    /// The file path to this loader assembly.
    /// Used to compute the relative path to the module's bin folder.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown if the loader assembly location is invalid or required directories do not exist.
    /// </exception>
    internal GenericAssemblyLoadContext(LoaderConfiguration config, string loaderAssemblyLocation)
        : base($"Svrooij_{config.ModuleName}_ALC", isCollectible: false)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        if (string.IsNullOrEmpty(loaderAssemblyLocation))
            throw new ArgumentException("Loader assembly location cannot be null or empty.", nameof(loaderAssemblyLocation));

        // Compute path to the bin folder relative to where this loader DLL is placed.
        // Expected layout after repackaging:
        //   module/
        //     bin/              (all DLLs consolidated here)
        //     loader.dll
        //     MicrosoftTeams.psd1
        //     ... other module files ...
        //
        string? loaderDir = Path.GetDirectoryName(loaderAssemblyLocation);
        if (loaderDir == null)
            throw new ArgumentException($"Cannot determine directory for loader assembly at '{loaderAssemblyLocation}'.", nameof(loaderAssemblyLocation));

        // The bin folder is typically in the same directory as the loader (module root)
        _binDirectory = Path.Combine(loaderDir, "bin");

        // Warn if bin directory doesn't exist, but don't fail (allow for flexible deployment scenarios)
        if (!Directory.Exists(_binDirectory))
        {
            System.Diagnostics.Debug.WriteLine(
                $"Warning: Bin directory '{_binDirectory}' does not exist. " +
                $"Module may not load correctly. Expected location: {_binDirectory}");
        }
    }

    /// <summary>
    /// Preloads the specified assemblies into this ALC from the bin folder.
    /// This ensures they are available before any code that depends on them runs.
    /// </summary>
    /// <param name="assemblyNames">The simple names of assemblies to preload (without .dll extension).</param>
    internal void Preload(params string[] assemblyNames)
    {
        foreach (string name in assemblyNames)
        {
            string path = Path.Combine(_binDirectory, $"{name}.dll");
            if (File.Exists(path))
            {
                try
                {
                    LoadFromAssemblyPath(path);
                }
                catch (Exception ex)
                {
                    // Log but don't fail; missing optional dependencies should not crash module import.
                    // In a full implementation, this would be logged via a logging service.
                    System.Diagnostics.Debug.WriteLine($"Failed to preload '{name}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Resolves an assembly from the bin folder, returning null if not found.
    /// Unlike <see cref="LoadFromAssemblyName"/>, this never falls back to the Default ALC,
    /// which would re-trigger the Default.Resolving event and cause infinite recursion.
    /// </summary>
    /// <param name="assemblyName">The assembly name to resolve.</param>
    /// <returns>The loaded assembly, or null if not found in the bin folder.</returns>
    internal Assembly? ResolveFromBin(AssemblyName assemblyName)
    {
        if (assemblyName?.Name == null)
            return null;

        string assemblyPath = Path.Combine(_binDirectory, $"{assemblyName.Name}.dll");
        return File.Exists(assemblyPath) ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    /// <summary>
    /// Overrides the load mechanism to resolve assemblies from the flat bin/ folder.
    /// All .dll files in the bin folder are automatically discovered and loaded.
    /// If not found in the folder, returns null to let the Default ALC handle it.
    /// </summary>
    /// <remarks>
    /// This approach is simpler and more robust than an explicit allow-list:
    /// - Automatically captures all transitive dependencies
    /// - No risk of forgetting to list a dependency
    /// - The folder boundary itself is the security perimeter (only bundled DLLs are there)
    /// </remarks>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName?.Name == null)
            return null;

        // First, try to load from the module's bin folder.
        // This ensures bundled assemblies are resolved from the private ALC.
        Assembly? assembly = ResolveFromBin(assemblyName);
        if (assembly != null)
            return assembly;

        // If not found in the bin folder, return null to let the Default ALC handle it.
        // This is crucial: framework assemblies (System.*) and PowerShell SDK assemblies
        // (System.Management.Automation) MUST come from Default ALC to maintain type identity.
        return null;
    }
}
