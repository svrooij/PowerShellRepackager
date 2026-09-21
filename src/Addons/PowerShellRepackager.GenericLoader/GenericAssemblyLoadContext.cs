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
    private readonly string[] _nativeSearchPaths;
    private readonly HashSet<string>? _isolatedAssemblies;
    private readonly Dictionary<string, IntPtr> _loadedNativeLibraries = new(StringComparer.OrdinalIgnoreCase);

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

        // The loader DLL is placed in {module}/bin; search paths in the config are relative to the module root.
        string? loaderDir = Path.GetDirectoryName(loaderAssemblyLocation);
        if (loaderDir == null)
            throw new ArgumentException($"Cannot determine directory for loader assembly at '{loaderAssemblyLocation}'.", nameof(loaderAssemblyLocation));

        _moduleDirectory = Path.GetFileName(loaderDir).Equals("bin", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(loaderDir) ?? loaderDir
            : loaderDir;

        // Determine assembly search paths from config or use default
        // Config may specify multiple paths like ["bin", "net48", "netcore3.1"]
        var searchPaths = new List<string>();
        if (_config.AssemblySearchPaths != null && _config.AssemblySearchPaths.Length > 0)
        {
            searchPaths.AddRange(_config.AssemblySearchPaths.Select(path => Path.Combine(_moduleDirectory, path)));
            System.Diagnostics.Debug.WriteLine(
                $"Configured assembly search paths: {string.Join(", ", _config.AssemblySearchPaths)}");
        }
        else
        {
            // Default to bin/ for backward compatibility
            searchPaths.Add(Path.Combine(_moduleDirectory, "bin"));
            System.Diagnostics.Debug.WriteLine("No assembly search paths configured, using default: bin/");
        }

        // Resolve NuGet-style runtimes/{rid}/... folders for the current OS + architecture only
        var nativeSearchPaths = new List<string>();
        if (_config.RuntimesPaths != null)
        {
            foreach (var runtimesRelative in _config.RuntimesPaths)
            {
                var runtimesDir = Path.Combine(_moduleDirectory, runtimesRelative);
                if (!Directory.Exists(runtimesDir))
                    continue;

                foreach (var rid in RuntimeIdentifiers.GetCompatibleRids())
                {
                    var ridDir = Path.Combine(runtimesDir, rid);
                    if (!Directory.Exists(ridDir))
                        continue;

                    var nativeDir = Path.Combine(ridDir, "native");
                    if (Directory.Exists(nativeDir))
                        nativeSearchPaths.Add(nativeDir);

                    // runtimes/{rid}/lib/{tfm}/*.dll: RID-specific managed assemblies
                    var libDir = Path.Combine(ridDir, "lib");
                    if (Directory.Exists(libDir))
                        searchPaths.AddRange(Directory.GetDirectories(libDir).Where(d => Directory.GetFiles(d, "*.dll").Length > 0));
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"RID '{RuntimeIdentifiers.Current}' native search paths: {string.Join(", ", nativeSearchPaths)}");
        }

        _searchPaths = searchPaths.ToArray();
        _nativeSearchPaths = nativeSearchPaths.ToArray();

        if (_config.IsolatedAssemblies != null && _config.IsolatedAssemblies.Length > 0)
        {
            _isolatedAssemblies = new HashSet<string>(
                _config.IsolatedAssemblies.Select(n => Path.GetFileNameWithoutExtension(n)),
                StringComparer.OrdinalIgnoreCase);
            System.Diagnostics.Debug.WriteLine(
                $"Isolating {_isolatedAssemblies.Count} dependency assemblies; module-owned assemblies load into the Default ALC");
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
            var simpleName = Path.GetFileNameWithoutExtension(name);
            if (!IsIsolated(simpleName))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping preload of module-owned assembly '{simpleName}' (loaded into Default ALC on demand).");
                continue;
            }

            // Search for the assembly in all configured paths
            bool found = false;
            foreach (var searchPath in _searchPaths)
            {
                string path = Path.Combine(searchPath, $"{simpleName}.dll");
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
    /// Returns true when the assembly is a dependency that must live in this private ALC.
    /// When no isolation list is configured, every bundled assembly is isolated.
    /// </summary>
    internal bool IsIsolated(string assemblyName)
        => _isolatedAssemblies == null || _isolatedAssemblies.Contains(assemblyName);

    /// <summary>
    /// Finds the file for an assembly in the configured search paths, or null if not bundled.
    /// </summary>
    private string? FindAssemblyFile(string assemblyName)
    {
        foreach (var searchPath in _searchPaths)
        {
            string assemblyPath = Path.Combine(searchPath, $"{assemblyName}.dll");
            if (File.Exists(assemblyPath))
                return assemblyPath;
        }

        return null;
    }

    /// <summary>
    /// Resolves a bundled assembly for the Default ALC's Resolving event.
    /// Isolated dependencies are loaded into this private ALC; module-owned assemblies are loaded into
    /// the Default ALC so their types are visible to PowerShell scripts.
    /// When multiple repackaged modules are imported, several loaders answer this event in registration
    /// order. To avoid serving a too-old copy first, this handler only answers when its bundled copy
    /// is at least the requested version; otherwise it returns null so another module's handler
    /// (which may carry a newer copy) gets a chance.
    /// Unlike <see cref="LoadFromAssemblyName"/>, this never falls back to the Default ALC's own
    /// resolution, which would re-trigger the Default.Resolving event and cause infinite recursion.
    /// </summary>
    /// <param name="assemblyName">The assembly name to resolve.</param>
    /// <returns>The loaded assembly, or null if not found (or too old) in any search path.</returns>
    internal Assembly? ResolveFromBin(AssemblyName assemblyName)
    {
        if (assemblyName?.Name == null)
            return null;

        if (IsIsolated(assemblyName.Name))
        {
            var isolatedPath = FindAssemblyFile(assemblyName.Name);
            if (isolatedPath == null || IsBundledCopyTooOld(isolatedPath, assemblyName))
                return null;

            return ResolveIsolated(assemblyName);
        }

        var existing = Default.Assemblies.FirstOrDefault(a => a.GetName().Name?.Equals(assemblyName.Name, StringComparison.OrdinalIgnoreCase) == true);
        if (existing != null)
        {
            // The Default ALC can only hold one assembly per simple name, so this is the only copy
            // anyone can get; warn when it is older than requested to aid diagnosis.
            var existingVersion = existing.GetName().Version;
            if (assemblyName.Version != null && existingVersion != null && existingVersion < assemblyName.Version)
            {
                Console.WriteLine(
                    $"Warning: '{assemblyName.Name}' {existingVersion} is already loaded in the Default ALC but {assemblyName.Version} was requested.");
            }

            return existing;
        }

        var path = FindAssemblyFile(assemblyName.Name);
        if (path == null || IsBundledCopyTooOld(path, assemblyName))
            return null;

        try
        {
            Console.WriteLine($"Loading module-owned assembly '{assemblyName.Name}' into Default ALC from '{path}'");
            return Default.LoadFromAssemblyPath(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load assembly '{assemblyName.Name}' from '{path}' into Default ALC: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns true when the bundled file at <paramref name="path"/> has a lower assembly version
    /// than the requested one. Used only for Default ALC Resolving requests: this module's own
    /// internal loads (via <see cref="Load"/>) always get the bundled copy regardless of version,
    /// because that is the version the original module was shipped and tested with.
    /// </summary>
    private static bool IsBundledCopyTooOld(string path, AssemblyName requested)
    {
        if (requested.Version == null)
            return false;

        try
        {
            var bundledVersion = AssemblyName.GetAssemblyName(path).Version;
            if (bundledVersion != null && bundledVersion < requested.Version)
            {
                Console.WriteLine(
                    $"Not serving '{requested.Name}' from '{path}': bundled version {bundledVersion} is older than requested {requested.Version}; deferring to other resolvers.");
                return true;
            }
        }
        catch (Exception ex)
        {
            // If the file's identity cannot be read, fall back to serving it (previous behavior).
            Console.WriteLine($"Could not read assembly identity from '{path}': {ex.Message}");
        }

        return false;
    }

    private Assembly? ResolveIsolated(AssemblyName assemblyName)
    {
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

        // Module-owned assemblies (not in the isolation list) must come from the Default ALC so there is
        // a single type identity shared with PowerShell. Returning null defers to Default, whose
        // Resolving handler will load the file from the module folder if needed.
        if (!IsIsolated(assemblyName.Name))
            return null;

        // Isolated dependencies are resolved from the module's search paths into this private ALC.
        Assembly? assembly = ResolveIsolated(assemblyName);
        if (assembly != null)
            return assembly;

        // If not found in any search path, return null to let the Default ALC handle it.
        // This is crucial: framework assemblies (System.*) and PowerShell SDK assemblies
        // (System.Management.Automation) MUST come from Default ALC to maintain type identity.
        return null;
    }

    /// <summary>
    /// Resolves native libraries (P/Invoke targets such as msalruntime.dll) from the RID-specific
    /// <c>runtimes/{rid}/native</c> folder for the current platform, then from the managed search paths.
    /// Returns <see cref="IntPtr.Zero"/> to fall back to the default OS probing when not found.
    /// </summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (string.IsNullOrEmpty(unmanagedDllName))
            return IntPtr.Zero;

        lock (_loadedNativeLibraries)
        {
            if (_loadedNativeLibraries.TryGetValue(unmanagedDllName, out var cached))
                return cached;

            foreach (var candidate in RuntimeIdentifiers.GetNativeFileNameCandidates(unmanagedDllName))
            {
                foreach (var dir in _nativeSearchPaths.Concat(_searchPaths))
                {
                    var path = Path.Combine(dir, candidate);
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        var handle = LoadUnmanagedDllFromPath(path);
                        _loadedNativeLibraries[unmanagedDllName] = handle;
                        System.Diagnostics.Debug.WriteLine($"Loaded native library '{unmanagedDllName}' from '{path}'");
                        return handle;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load native library '{unmanagedDllName}' from '{path}': {ex.Message}");
                    }
                }
            }
        }

        return IntPtr.Zero;
    }
}
