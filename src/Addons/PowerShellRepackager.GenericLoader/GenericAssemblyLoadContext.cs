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
    private readonly string _moduleDirectory;
    private readonly string[] _searchPaths;

    /// <summary>
    /// Initializes a new instance of the generic ALC with the given configuration.
    /// </summary>
    /// <param name="config">The loader configuration for this module.</param>
    /// <param name="loaderAssemblyLocation">
    /// The file path to this loader assembly.
    /// Used to compute the relative paths for assembly search directories.
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

        // Compute path to the module directory (where the loader DLL is placed)
        string? loaderDir = Path.GetDirectoryName(loaderAssemblyLocation);
        if (loaderDir == null)
            throw new ArgumentException($"Cannot determine directory for loader assembly at '{loaderAssemblyLocation}'.", nameof(loaderAssemblyLocation));

        _moduleDirectory = loaderDir;

        // Determine assembly search paths from config or use default
        // Config may specify multiple paths like ["bin", "net48", "netcore3.1"]
        if (_config.AssemblySearchPaths != null && _config.AssemblySearchPaths.Length > 0)
        {
            _searchPaths = _config.AssemblySearchPaths
                .Select(path => Path.Combine(_moduleDirectory, path))
                .ToArray();
            System.Diagnostics.Debug.WriteLine(
                $"Configured assembly search paths: {string.Join(", ", _config.AssemblySearchPaths)}");
        }
        else
        {
            // Default to bin/ for backward compatibility
            _searchPaths = new[] { Path.Combine(_moduleDirectory, "bin") };
            System.Diagnostics.Debug.WriteLine("No assembly search paths configured, using default: bin/");
        }

        // Warn if none of the search directories exist
        var anyExists = _searchPaths.Any(Directory.Exists);
        if (!anyExists)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Warning: None of the configured assembly search paths exist: {string.Join(", ", _searchPaths)}. " +
                $"Module may not load correctly.");
        }
    }

    /// <summary>
    /// Preloads the specified assemblies into this ALC from the configured search paths.
    /// This ensures they are available before any code that depends on them runs.
    /// </summary>
    /// <param name="assemblyNames">The simple names of assemblies to preload (without .dll extension).</param>
    internal void Preload(params string[] assemblyNames)
    {
        foreach (string name in assemblyNames)
        {
            // Search for the assembly in all configured paths
            bool found = false;
            foreach (var searchPath in _searchPaths)
            {
                string path = Path.Combine(searchPath, $"{name}.dll");
                if (File.Exists(path))
                {
                    try
                    {
                        LoadFromAssemblyPath(path);
                        found = true;
                        break; // Found and loaded, no need to search other paths
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail; missing optional dependencies should not crash module import.
                        System.Diagnostics.Debug.WriteLine($"Failed to preload '{name}' from '{path}': {ex.Message}");
                    }
                }
            }

            if (!found)
            {
                System.Diagnostics.Debug.WriteLine($"Preload assembly '{name}' not found in any search path.");
            }
        }
    }

    /// <summary>
    /// Resolves an assembly from the configured search paths, returning null if not found.
    /// Unlike <see cref="LoadFromAssemblyName"/>, this never falls back to the Default ALC,
    /// which would re-trigger the Default.Resolving event and cause infinite recursion.
    /// </summary>
    /// <param name="assemblyName">The assembly name to resolve.</param>
    /// <returns>The loaded assembly, or null if not found in any search path.</returns>
    internal Assembly? ResolveFromBin(AssemblyName assemblyName)
    {
        if (assemblyName?.Name == null)
            return null;

        // Search for the assembly in all configured paths
        foreach (var searchPath in _searchPaths)
        {
            string assemblyPath = Path.Combine(searchPath, $"{assemblyName.Name}.dll");
            if (File.Exists(assemblyPath))
            {
                try
                {
                    return LoadFromAssemblyPath(assemblyPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to load assembly '{assemblyName.Name}' from '{assemblyPath}': {ex.Message}");
                    // Continue searching in other paths or return null
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Overrides the load mechanism to resolve assemblies from the configured search paths.
    /// All .dll files in the configured paths are automatically discoverable and loaded.
    /// If not found in any search path, returns null to let the Default ALC handle it.
    /// </summary>
    /// <remarks>
    /// This approach supports multiple folder structures:
    /// - Flat bin/ layout (backward compatible)
    /// - Framework-specific folders like net48/, netcore3.1/ (preserved original structure)
    /// - Mixed layouts with multiple search paths
    /// 
    /// All bundled DLLs are discovered automatically from configured paths.
    /// No risk of forgetting to list a dependency; the folder boundary is the security perimeter.
    /// Framework assemblies (System.*, System.Management.Automation) and PowerShell SDK assemblies
    /// remain resolvable from the Default ALC, maintaining type compatibility.
    /// </remarks>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName?.Name == null)
            return null;

        // First, try to load from the module's search paths.
        // This ensures bundled assemblies are resolved from the private ALC.
        Assembly? assembly = ResolveFromBin(assemblyName);
        if (assembly != null)
            return assembly;

        // If not found in any search path, return null to let the Default ALC handle it.
        // This is crucial: framework assemblies (System.*) and PowerShell SDK assemblies
        // (System.Management.Automation) MUST come from Default ALC to maintain type identity.
        return null;
    }
}
