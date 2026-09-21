using System.IO.Compression;
using Microsoft.Extensions.Logging;
using PowerShellRepackager.Models;

namespace PowerShellRepackager.Mechanics;

/// <summary>
/// Extracts and normalizes the contents of a .nupkg (module package) file.
/// </summary>
/// <remarks>
/// A .nupkg is a standard ZIP archive containing a PowerShell module and its dependencies.
/// This class:
/// 1. Extracts the archive to a working directory
/// 2. Identifies the manifest, root module, and bundled assemblies
/// 3. Detects the highest-priority .NET TFM folder (e.g., net8.0)
/// 4. Filters out .NET Framework folders (net472, net48, desktop, core)
/// 5. Returns a normalized <see cref="ModulePackageInfo"/> model
/// </remarks>
internal class PackageExtractor
{
    private readonly ILogger<PackageExtractor> _logger;

    private static readonly string WorkDirectory = Path.Combine(Path.GetTempPath(), "PowerShellRepackager", "work");

    // .NET Framework / legacy folders to skip (not compatible with PS 7.4+)
    private static readonly string[] FrameworkFolderPrefixes = ["net472", "net48", "netframework", "desktop", "core"];

    public PackageExtractor(ILogger<PackageExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        _logger = logger;
    }

    /// <summary>
    /// Extracts a .nupkg file and returns normalized package information.
    /// </summary>
    /// <param name="nupkgPath">Full path to the .nupkg file.</param>
    /// <param name="moduleName">The module name (used for organizing the work directory).</param>
    /// <param name="version">The module version (used for organizing the work directory).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ModulePackageInfo"/> with extracted and normalized package contents.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the nupkg file doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the package is malformed or has no compatible TFM.</exception>
    public Task<ModulePackageInfo> ExtractPackageAsync(string nupkgPath, string moduleName, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nupkgPath, nameof(nupkgPath));
        ArgumentNullException.ThrowIfNull(moduleName, nameof(moduleName));
        ArgumentNullException.ThrowIfNull(version, nameof(version));

        if (!File.Exists(nupkgPath))
            throw new FileNotFoundException($"Package file not found: {nupkgPath}", nupkgPath);

        // Use synchronous extraction for now; could be async with appropriate wrapper
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Extracting module package from {NupkgPath}", nupkgPath);

        var extractedPath = Path.Combine(WorkDirectory, moduleName, version);
        if (Directory.Exists(extractedPath))
        {
            _logger.LogInformation("Cleaning up existing extraction directory {Path}", extractedPath);
            Directory.Delete(extractedPath, recursive: true);
        }

        Directory.CreateDirectory(extractedPath);

        // Extract the .nupkg (which is a ZIP file)
        using (var archive = ZipFile.OpenRead(nupkgPath))
        {
            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.Combine(extractedPath, entry.FullName);

                // Create directories as needed
                if (entry.FullName.EndsWith("/"))
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }
        }

        _logger.LogInformation("Package extracted to {Path}", extractedPath);

        // Parse and normalize the extracted content
        var packageInfo = NormalizeExtractedPackage(moduleName, version, extractedPath);

        _logger.LogInformation(
            "Package normalized: TFM={Tfm}, Files={FileCount}, Assemblies={AssemblyCount}",
            packageInfo.SelectedTargetFramework.ToFolderName(),
            packageInfo.Files.Count,
            packageInfo.Assemblies.Count);

        return Task.FromResult(packageInfo);
    }

    /// <summary>
    /// Normalizes an extracted package by identifying files, detecting TFM, and filtering incompatible content.
    /// </summary>
    private ModulePackageInfo NormalizeExtractedPackage(string moduleName, string version, string extractedPath)
    {
        // Find the manifest file
        var manifestFile = FindManifestFile(extractedPath, moduleName);
        if (manifestFile == null)
            throw new InvalidOperationException($"No module manifest (.psd1) found in {extractedPath}");

        _logger.LogDebug("Found manifest: {ManifestPath}", manifestFile.FullPath);

        // Discover all TFM folders in bin/
        var tfmFolders = DiscoverTargetFrameworks(extractedPath);
        if (tfmFolders.Count == 0)
        {
            throw new InvalidOperationException(
                $"No compatible .NET target frameworks found. Module must contain one of: " +
                $"netcore3.1 (or netcoreapp3.1), net5.0, net6.0, net7.0, net8.0, net9.0, netCore");
        }

        // Select the highest priority TFM
        var selectedTfm = tfmFolders.OrderByDescending(tf => tf.GetVersionPriority()).First();
        _logger.LogInformation("Selected target framework: {Tfm}", selectedTfm.ToFolderName());

        // Gather all files
        var files = new List<ModuleFile>();

        // Add manifest
        files.Add(new ModuleFile
        {
            RelativePath = Path.GetFileName(manifestFile.FullPath),
            FileType = ModuleFileType.Manifest,
            FullPath = manifestFile.FullPath,
        });

        // Scan for assemblies in the selected TFM folder
        ScanDirectoryForFiles(extractedPath, selectedTfm, files);

        // Scan root and lib for other content
        ScanDirectoryForScriptsAndData(extractedPath, manifestFile.FullPath, files);

        return new ModulePackageInfo
        {
            ModuleName = moduleName,
            Version = version,
            ExtractedPath = extractedPath,
            SelectedTargetFramework = selectedTfm,
            Files = files.AsReadOnly(),
        };
    }

    /// <summary>
    /// Finds the module manifest file (.psd1).
    /// Prefers the manifest matching the module name (e.g. MicrosoftTeams.psd1), since packages
    /// may ship additional manifests for nested/sub modules.
    /// </summary>
    private ModuleFile? FindManifestFile(string extractedPath, string moduleName)
    {
        var manifestFiles = Directory.GetFiles(extractedPath, "*.psd1", SearchOption.TopDirectoryOnly);
        if (manifestFiles.Length == 0)
            return null;

        var manifestPath = manifestFiles.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Equals(moduleName, StringComparison.OrdinalIgnoreCase));

        if (manifestPath == null)
        {
            manifestPath = manifestFiles[0];
            if (manifestFiles.Length > 1)
            {
                _logger.LogWarning("No manifest named {ModuleName}.psd1 found; using first of {Count}: {Manifest}",
                    moduleName, manifestFiles.Length, Path.GetFileName(manifestPath));
            }
        }

        return new ModuleFile
        {
            RelativePath = Path.GetFileName(manifestPath),
            FileType = ModuleFileType.Manifest,
            FullPath = manifestPath,
        };
    }

    /// <summary>
    /// Discovers all supported .NET target frameworks in the extracted package.
    /// Looks for TFM folders at the root level of the extraction (e.g., "netcoreapp3.1", "net472").
    /// Recognizes both naming conventions: "netcore3.1" and "netcoreapp3.1" for .NET Core 3.1,
    /// and standard naming like "net5.0", "net6.0", etc. for later versions.
    /// </summary>
    private List<TargetFramework> DiscoverTargetFrameworks(string extractedPath)
    {
        var tfms = new List<TargetFramework>();

        _logger.LogDebug("Scanning extracted package for TFM folders at root: {ExtractedPath}", extractedPath);

        var subdirs = Directory.GetDirectories(extractedPath);
        _logger.LogDebug("Found {Count} subdirectories at root level", subdirs.Length);

        foreach (var subdir in subdirs)
        {
            var folderName = Path.GetFileName(subdir);
            _logger.LogDebug("Examining folder: {FolderName} (full path: {FullPath})", folderName, subdir);

            // Skip known non-TFM folders
            if (folderName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("_manifest", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("_rels", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("package", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("exports", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("internal", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("custom", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping known non-TFM folder: {Folder}", folderName);
                continue;
            }

            // Skip .NET Framework folders (net472, etc.)
            if (FrameworkFolderPrefixes.Any(prefix => folderName!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("Skipping .NET Framework folder: {Folder}", folderName);
                continue;
            }

            // Try to parse as a supported TFM (handles netcore3.1, netcoreapp3.1, net5.0, etc.)
            // TryParseFolderName now does case-insensitive matching
            if (TargetFrameworkExtensions.TryParseFolderName(folderName, out var tf))
            {
                tfms.Add(tf);
                _logger.LogInformation("Discovered target framework: {Tfm} (folder: {Folder})", tf.ToFolderName(), folderName);
            }
            else
            {
                _logger.LogDebug("Unrecognized framework folder (skipped): {Folder}", folderName);
            }
        }

        if (tfms.Count == 0)
        {
            _logger.LogWarning("No compatible target frameworks found in {ExtractedPath}. Root subdirectories were: {Subdirs}",
                extractedPath, string.Join(", ", subdirs.Select(Path.GetFileName)));
        }

        return tfms;
    }

    /// <summary>
    /// Scans the package for assemblies in both the flat /bin folder and the selected TFM folder.
    /// TFM folders are located at the root of the extracted package (e.g., "netcoreapp3.1").
    /// Handles both folder naming conventions: "netcore3.1" and "netcoreapp3.1" for .NET Core 3.1.
    /// </summary>
    private void ScanDirectoryForFiles(string extractedPath, TargetFramework selectedTfm, List<ModuleFile> files)
    {
        // First, scan the flat /bin folder for shared DLLs
        var binPath = Path.Combine(extractedPath, "bin");
        if (Directory.Exists(binPath))
        {
            _logger.LogDebug("Scanning flat bin folder for shared assemblies: {Path}", binPath);
            var flatDllFiles = Directory.GetFiles(binPath, "*.dll", SearchOption.TopDirectoryOnly);
            _logger.LogDebug("Found {Count} shared DLL files in flat bin/", flatDllFiles.Length);

            foreach (var dllPath in flatDllFiles)
            {
                var relativePath = Path.GetRelativePath(extractedPath, dllPath);
                files.Add(new ModuleFile
                {
                    RelativePath = relativePath,
                    FileType = ModuleFileType.Assembly,
                    FullPath = dllPath,
                });
            }
        }

        // Then, scan the selected TFM folder for TFM-specific DLLs.
        // Resolve the folder by parsing directory names so alternative spellings/casing
        // (netcoreapp3.1 vs netcore3.1, netCore vs netcore) work on case-sensitive file systems too.
        var selectedTfmFolder = Directory.GetDirectories(extractedPath)
            .FirstOrDefault(d => TargetFrameworkExtensions.TryParseFolderName(Path.GetFileName(d), out var tf) && tf == selectedTfm)
            ?? Path.Combine(extractedPath, selectedTfm.ToFolderName());

        _logger.LogDebug("Looking for TFM-specific folder: {Path}", selectedTfmFolder);

        if (!Directory.Exists(selectedTfmFolder))
        {
            _logger.LogWarning("Selected TFM folder does not exist: {Path}", selectedTfmFolder);
            return;
        }

        // Recursively find all .dll files in this TFM folder
        var tfmDllFiles = Directory.GetFiles(selectedTfmFolder, "*.dll", SearchOption.AllDirectories);
        _logger.LogDebug("Found {Count} TFM-specific DLL files in {Path}", tfmDllFiles.Length, selectedTfmFolder);

        foreach (var dllPath in tfmDllFiles)
        {
            var relativePath = Path.GetRelativePath(extractedPath, dllPath);
            files.Add(new ModuleFile
            {
                RelativePath = relativePath,
                FileType = ModuleFileType.Assembly,
                FullPath = dllPath,
            });
        }

        _logger.LogDebug("Total assemblies added: {Count}", files.Count(f => f.FileType == ModuleFileType.Assembly));
    }

    /// <summary>
    /// Scans the root and lib directories for scripts and data files.
    /// Secondary manifests (e.g. nested sub-module .psd1 files imported by the root .psm1) are kept;
    /// only the main module manifest is excluded because it is rewritten separately.
    /// </summary>
    private void ScanDirectoryForScriptsAndData(string extractedPath, string mainManifestPath, List<ModuleFile> files)
    {
        var excludedDirs = new[] { "bin", "_rels", "package" };

        var rootFiles = Directory.EnumerateFiles(extractedPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Equals(mainManifestPath, StringComparison.OrdinalIgnoreCase));

        foreach (var file in rootFiles)
        {
            var relativePath = Path.GetRelativePath(extractedPath, file);
            var extension = Path.GetExtension(file).ToLowerInvariant();

            var fileType = extension switch
            {
                ".ps1" or ".ps1xml" or ".psm1" or ".psd1" => ModuleFileType.Script,
                ".md" or ".txt" or ".license" => ModuleFileType.Data,
                _ => ModuleFileType.Other,
            };

            files.Add(new ModuleFile
            {
                RelativePath = relativePath,
                FileType = fileType,
                FullPath = file,
            });
        }

        // Recursively scan subdirectories
        foreach (var subdir in Directory.GetDirectories(extractedPath))
        {
            var folderName = Path.GetFileName(subdir);
            if (excludedDirs.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                continue;

            if (FrameworkFolderPrefixes.Any(prefix => folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("Skipping .NET Framework folder for scripts/data: {Folder}", folderName);
                continue;
            }

            RecursivelyAddFiles(subdir, extractedPath, files);
        }
    }

    /// <summary>
    /// Recursively scans a directory for script and data files.
    /// </summary>
    private void RecursivelyAddFiles(string directory, string basePath, List<ModuleFile> files)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();

                // Skip binaries (already handled in TFM scan) and NuGet metadata
                if (extension == ".dll" || extension == ".nuspec")
                    continue;

                var relativePath = Path.GetRelativePath(basePath, file);
                var fileType = extension switch
                {
                    ".ps1" or ".ps1xml" or ".psm1" or ".psd1" => ModuleFileType.Script,
                    ".md" or ".txt" or ".license" => ModuleFileType.Data,
                    _ => ModuleFileType.Other,
                };

                files.Add(new ModuleFile
                {
                    RelativePath = relativePath,
                    FileType = fileType,
                    FullPath = file,
                });
            }

            // Recurse into subdirectories
            foreach (var subdir in Directory.GetDirectories(directory))
            {
                RecursivelyAddFiles(subdir, basePath, files);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied while scanning {Directory}: {Message}", directory, ex.Message);
        }
    }
}
