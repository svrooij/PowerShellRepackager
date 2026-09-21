using Microsoft.Extensions.Logging;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerShellRepackager.Models;

namespace PowerShellRepackager.Mechanics;

/// <summary>
/// Handles the repackaging process (Steps 3-5): consolidating extracted module files,
/// parsing and rewriting the manifest, generating loader configuration, and organizing
/// the final module output structure.
/// </summary>
public class ModuleRepackager
{
    private readonly ILogger<ModuleRepackager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleRepackager"/> class with the specified logger.
    /// </summary>
    /// <param name="logger"></param>
    public ModuleRepackager(ILogger<ModuleRepackager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Repackages an extracted module by consolidating files, parsing/rewriting manifest,
    /// generating loader config, and organizing output structure.
    /// </summary>
    /// <param name="extractedInfo">Module extraction result from PackageExtractor</param>
    /// <param name="repackagedModuleName">Name for the new module (e.g., "Svrooij.MicrosoftTeams")</param>
    /// <param name="outputPath">Base output directory. If null or empty, uses current directory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Repackaging result with output directory and file information</returns>
    public async Task<ModuleRepackageInfo> RepackageModuleAsync(
        ModulePackageInfo extractedInfo,
        string repackagedModuleName,
        string? outputPath,
        CancellationToken cancellationToken = default)
        => await RepackageModuleAsync(extractedInfo, repackagedModuleName, outputPath, null, null, null, cancellationToken);

    /// <summary>
    /// Repackages a module, with explicit overrides for assembly isolation.
    /// </summary>
    /// <param name="extractedInfo">Module extraction result from PackageExtractor</param>
    /// <param name="repackagedModuleName">Name for the new module (e.g., "Svrooij.MicrosoftTeams")</param>
    /// <param name="outputPath">Base output directory. If null or empty, uses current directory</param>
    /// <param name="repackagedModuleVersion">Version for the new module (e.g., "1.0.0")</param>
    /// <param name="isolateAssemblies">Assembly names or wildcard patterns that must be isolated in the private ALC.</param>
    /// <param name="sharedAssemblies">Assembly names or wildcard patterns that must be loaded into the Default ALC.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<ModuleRepackageInfo> RepackageModuleAsync(
        ModulePackageInfo extractedInfo,
        string repackagedModuleName,
        string? outputPath,
        string? repackagedModuleVersion,
        string[]? isolateAssemblies,
        string[]? sharedAssemblies,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting repackaging: {Module} v{Version} → {NewName}",
            extractedInfo.ModuleName, extractedInfo.Version, repackagedModuleName);

        var effectivePackageVersion = string.IsNullOrWhiteSpace(repackagedModuleVersion)
            ? extractedInfo.Version
            : repackagedModuleVersion;

        // Determine output base directory
        var baseOutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Environment.CurrentDirectory
            : outputPath;

        // Create output directory structure: {BaseOutput}/{OriginalName}-{Version}/{NewModuleName}/
        var moduleOutputDir = Path.Combine(
            baseOutputPath,
            $"{extractedInfo.ModuleName}-{extractedInfo.Version}",
            repackagedModuleName);

        var binDir = Path.Combine(moduleOutputDir, "bin");

        // Clean and create directories
        if (Directory.Exists(moduleOutputDir))
        {
            _logger.LogWarning("Output directory already exists; overwriting: {Path}", moduleOutputDir);
            Directory.Delete(moduleOutputDir, recursive: true);
        }

        Directory.CreateDirectory(binDir);
        _logger.LogInformation("Created output directory: {Path}", moduleOutputDir);

        // Parse the original manifest to understand module structure
        var manifestPath = extractedInfo.ManifestFile?.FullPath;
        var manifestData = await ParseManifestAsync(manifestPath, cancellationToken);

        // A .psd1 has no ModuleName key (the name is the file name), so fall back to the package metadata
        if (string.IsNullOrEmpty(manifestData.ModuleName) || manifestData.ModuleName == "UnknownModule")
        {
            manifestData.ModuleName = extractedInfo.ModuleName;
        }
        if (string.IsNullOrEmpty(manifestData.ModuleVersion) || manifestData.ModuleVersion == "1.0.0")
        {
            manifestData.ModuleVersion = extractedInfo.Version;
        }

        _logger.LogInformation("Parsed manifest: {Module} v{Version}, RootModule: {RootModule}",
            manifestData.ModuleName, manifestData.ModuleVersion, manifestData.RootModule ?? "(none)");

        // Consolidate assemblies while preserving folder structure
        var assemblyCount = await ConsolidateAssembliesAsync(
            extractedInfo,
            moduleOutputDir,
            cancellationToken);
        _logger.LogInformation("Consolidated {Count} assemblies while preserving folder structure", assemblyCount);

        // Copy non-assembly module files (scripts, data, etc.)
        var fileCount = await CopyModuleFilesAsync(
            extractedInfo,
            moduleOutputDir,
            binDir,
            cancellationToken);

        // Generate new module GUID (repackaging creates a new identity), first four chars set to 2807
        var newModuleGuid = Guid.Parse("2807" + Guid.NewGuid().ToString("D")[4..]);

        // Each repackaged module gets its own uniquely named copy of the generic loader, so multiple
        // repackaged modules can be imported into the same PowerShell session without conflicts.
        var loaderAssemblyName = AssemblyRenamer.BuildLoaderAssemblyName(extractedInfo.ModuleName);
        var loaderFileName = $"{loaderAssemblyName}.dll";

        // Rewrite the manifest for the new module
        var rewrittenManifestPath = await RewriteManifestAsync(
            manifestPath,
            manifestData,
            moduleOutputDir,
            repackagedModuleName,
            newModuleGuid,
            effectivePackageVersion,
            loaderFileName,
            cancellationToken);
        _logger.LogInformation("Rewrote manifest: {Path}", rewrittenManifestPath);

        // Write attribution document (and copy the original license when bundled)
        await WriteAttributionAsync(extractedInfo, manifestData, moduleOutputDir, repackagedModuleName, cancellationToken);

        // Determine assemblies to preload (critical system/framework DLLs)
        var preloadAssemblies = DeterminePreloadAssemblies(extractedInfo.Assemblies);

        // Determine which bundled assemblies are dependencies (isolated in the private ALC) versus
        // module-owned (loaded into the Default ALC so PowerShell scripts can resolve their types)
        var isolatedAssemblies = await DetermineIsolatedAssembliesAsync(moduleOutputDir, isolateAssemblies, sharedAssemblies, loaderAssemblyName, cancellationToken);

        // Generate loader configuration
        var loaderConfigPath = await GenerateLoaderConfigAsync(
            binDir,
            repackagedModuleName,
            preloadAssemblies,
            isolatedAssemblies,
            cancellationToken);
        _logger.LogInformation("Generated loader config: {Path}", loaderConfigPath);

        // Copy the generic loader DLL to bin/ under its per-module name and rename its assembly identity
        var loaderDllPath = await CopyGenericLoaderAsync(binDir, loaderAssemblyName, cancellationToken);
        _logger.LogInformation("Copied generic loader: {Path}", loaderDllPath);

        // Collect output statistics
        var allFiles = Directory.GetFiles(moduleOutputDir, "*", SearchOption.AllDirectories);

        var result = new ModuleRepackageInfo
        {
            OriginalModuleName = extractedInfo.ModuleName,
            OriginalModuleVersion = extractedInfo.Version,
            RepackagedModuleName = repackagedModuleName,
            OutputDirectory = moduleOutputDir,
            BinDirectory = binDir,
            ManifestPath = rewrittenManifestPath,
            LoaderConfigPath = loaderConfigPath,
            LoaderDllPath = loaderDllPath,
            AssemblyCount = assemblyCount,
            TotalFileCount = allFiles.Length,
            RepackagedModuleGuid = newModuleGuid,
            SelectedTargetFramework = extractedInfo.SelectedTargetFramework,
            RepackageDate = DateTime.UtcNow,
            PreloadAssemblies = preloadAssemblies,
            IncludedFiles = allFiles
        };

        _logger.LogInformation("Repackaging complete: {Summary}", result.GetSummary());
        return result;
    }

    /// <summary>
    /// Parses the PowerShell module manifest (.psd1) to extract metadata.
    /// </summary>
    private async Task<ManifestData> ParseManifestAsync(
        string? manifestPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
        {
            _logger.LogWarning("Manifest file not found: {Path}", manifestPath);
            return new ManifestData
            {
                ModuleName = "UnknownModule",
                ModuleVersion = "1.0.0",
                IsBinaryModule = false
            };
        }

        try
        {
            var manifestContent = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            manifestContent = ResolvePSEditionConditionals(manifestContent);

            // Use PowerShell AST to parse the manifest hash table
            var parseErrors = Array.Empty<ParseError>();
            var ast = Parser.ParseInput(manifestContent, out var tokens, out parseErrors);

            if (parseErrors.Length > 0)
            {
                _logger.LogWarning("Manifest parse warnings: {Count} errors", parseErrors.Length);
            }

            var manifestData = new ManifestData
            {
                // A manifest has no ModuleName key: the module name is the file name
                ModuleName = Path.GetFileNameWithoutExtension(manifestPath),
                ModuleVersion = "1.0.0",
                ManifestPath = manifestPath,
                IsBinaryModule = false
            };

            // Extract key-value pairs from the hashtable
            ExtractManifestValues(manifestContent, manifestData);

            return manifestData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing manifest: {Path}", manifestPath);
            return new ManifestData
            {
                ModuleName = "UnknownModule",
                ModuleVersion = "1.0.0",
                ManifestPath = manifestPath,
                IsBinaryModule = false
            };
        }
    }

    /// <summary>
    /// Extracts key manifest values using regex and string parsing.
    /// </summary>
    private void ExtractManifestValues(string manifestContent, ManifestData data)
    {
        // Note: ModuleName is intentionally not read from the content. Manifests don't have that key at the
        // root; any 'ModuleName =' found belongs to a RequiredModules/NestedModules entry.

        // Extract ModuleVersion
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "ModuleVersion\\s*=\\s*['\"]?([^'\"\\n]+)['\"]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (versionMatch.Success)
        {
            data.ModuleVersion = versionMatch.Groups[1].Value.Trim();
        }

        // Extract RootModule
        var rootModuleMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "RootModule\\s*=\\s*['\"]?([^'\"\\n]+)['\"]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (rootModuleMatch.Success)
        {
            var rootModule = rootModuleMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(rootModule))
            {
                data.RootModule = rootModule;
                data.IsBinaryModule = rootModule.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Extract GUID
        var guidMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "GUID\\s*=\\s*['\"]?([a-f0-9-]{36})['\"]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (guidMatch.Success && Guid.TryParse(guidMatch.Groups[1].Value, out var guid))
        {
            data.ModuleGuid = guid;
        }

        // Extract Author
        var authorMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "Author\\s*=\\s*['\"]?([^'\"\\n]+)['\"]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (authorMatch.Success)
        {
            data.Author = authorMatch.Groups[1].Value.Trim();
        }

        // Extract CompanyName
        var companyMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "CompanyName\\s*=\\s*['\"]?([^'\"\\n]+)['\"]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (companyMatch.Success)
        {
            data.CompanyName = companyMatch.Groups[1].Value.Trim();
        }

        // Extract Description
        var descMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "Description\\s*=\\s*['\"]?([^'\"\\n]+)['\"]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (descMatch.Success)
        {
            data.Description = descMatch.Groups[1].Value.Trim();
        }

        // Extract PowerShellVersion
        var psVersionMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "PowerShellVersion\\s*=\\s*['\"]?([^'\"\\n]+)['\"]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (psVersionMatch.Success)
        {
            data.PowerShellVersion = psVersionMatch.Groups[1].Value.Trim();
        }

        data.Copyright = ExtractScalar(manifestContent, "Copyright");
        data.ProjectUri = ExtractScalar(manifestContent, "ProjectUri");
        data.LicenseUri = ExtractScalar(manifestContent, "LicenseUri");
        data.IconUri = ExtractScalar(manifestContent, "IconUri");
        data.HelpInfoUri = ExtractScalar(manifestContent, "HelpInfoURI");
        data.Tags = ExtractArray(manifestContent, "Tags");

        // Store export information if needed
        _logger.LogDebug("Extracted manifest data - Module: {ModuleName} v{Version}, RootModule: {RootModule}, IsBinaryModule: {IsBinary}",
            data.ModuleName, data.ModuleVersion, data.RootModule, data.IsBinaryModule);
    }

    /// <summary>
    /// Reads a quoted scalar value from the manifest (any nesting level), ignoring commented-out lines.
    /// </summary>
    private static string? ExtractScalar(string manifestContent, string field)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            manifestContent,
            $@"(?m)^(?!\s*#)\s*{System.Text.RegularExpressions.Regex.Escape(field)}\s*=\s*(['""])(.*?)\1\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var value = match.Groups[2].Value.Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Reads a string array value from the manifest (any nesting level), ignoring commented-out lines.
    /// </summary>
    private static string[] ExtractArray(string manifestContent, string field)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            manifestContent,
            $@"(?ms)^(?!\s*#)\s*{System.Text.RegularExpressions.Regex.Escape(field)}\s*=\s*(@\((?<body>.*?)\)|(?<body>[^\r\n]+))\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return [];

        var body = System.Text.RegularExpressions.Regex.Replace(match.Groups["body"].Value, @"(?m)^\s*#.*$", string.Empty);
        return System.Text.RegularExpressions.Regex.Matches(body, @"(['""])(.*?)\1")
            .Select(m => m.Groups[2].Value.Trim())
            .Where(v => v.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Consolidates all assemblies (DLLs) from extracted package into flat /bin directory.
    /// Copies from both the selected TFM folder and the flat /bin of the extracted package.
    /// </summary>
    private async Task<int> ConsolidateAssembliesAsync(
        ModulePackageInfo extractedInfo,
        string moduleOutputDir,
        CancellationToken cancellationToken)
    {
        int copiedCount = 0;
        int skippedCount = 0;

        // Copy all DLLs while preserving their relative folder structure
        // This ensures .psm1 scripts that reference specific framework folders (e.g., netcore3.1/)
        // will continue to work after repackaging
        if (extractedInfo.Assemblies != null && extractedInfo.Assemblies.Count > 0)
        {
            foreach (var assembly in extractedInfo.Assemblies)
            {
                if (!File.Exists(assembly.FullPath))
                {
                    _logger.LogWarning("Assembly file not found: {Path}", assembly.FullPath);
                    continue;
                }

                // Skip framework assemblies that should come from the PowerShell runtime
                // These assemblies are provided by PowerShell and should never be bundled
                var assemblyName = Path.GetFileNameWithoutExtension(assembly.FullPath);
                if (IsFrameworkAssembly(assemblyName))
                {
                    skippedCount++;
                    _logger.LogDebug("Skipped framework assembly: {FileName}", assemblyName);
                    continue;
                }

                // Use the relative path to preserve folder structure
                // e.g., "bin/netcore3.1/SomeLib.dll" → stays in the same folder
                var destPath = Path.Combine(moduleOutputDir, assembly.RelativePath);
                var destDir = Path.GetDirectoryName(destPath);

                if (string.IsNullOrEmpty(destDir))
                {
                    _logger.LogWarning("Invalid destination directory for assembly: {Path}", assembly.FullPath);
                    continue;
                }

                // Ensure destination directory exists
                Directory.CreateDirectory(destDir);

                try
                {
                    // Use async copy
                    await using var sourceStream = File.OpenRead(assembly.FullPath);
                    await using var destStream = File.Create(destPath);
                    await sourceStream.CopyToAsync(destStream, cancellationToken);

                    copiedCount++;
                    _logger.LogDebug("Copied assembly preserving path: {RelativePath}", assembly.RelativePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error copying assembly: {Path}", assembly.FullPath);
                }
            }
        }

        if (skippedCount > 0)
        {
            _logger.LogInformation("Skipped {Count} framework assemblies (provided by PowerShell runtime)", skippedCount);
        }

        return copiedCount;
    }

    /// <summary>
    /// Determines if an assembly is a framework assembly that should be provided by the PowerShell runtime.
    /// Framework assemblies should never be bundled in a repackaged module.
    /// </summary>
    /// <param name="assemblyName">The simple name of the assembly (without .dll extension)</param>
    /// <returns>True if the assembly is a framework assembly; false otherwise</returns>
    private static bool IsFrameworkAssembly(string assemblyName)
    {
        // Framework assemblies that are provided by PowerShell and should not be bundled
        var frameworkAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // PowerShell SDK assemblies
            "System.Management.Automation",
            "System.Management",

            // System.* assemblies (these come from .NET runtime)
            "System.Collections",
            "System.Collections.Concurrent",
            "System.Collections.Generic",
            "System.Collections.Immutable",
            "System.Collections.NonGeneric",
            "System.Collections.Specialized",
            "System.ComponentModel",
            "System.ComponentModel.Annotations",
            "System.ComponentModel.DataAnnotations",
            "System.ComponentModel.Primitives",
            "System.ComponentModel.TypeConverter",
            "System.Configuration.ConfigurationManager",
            "System.Console",
            "System.Core",
            "System.Data",
            "System.Data.Common",
            "System.Data.SqlClient",
            "System.Diagnostics.Debug",
            "System.Diagnostics.DiagnosticSource",
            "System.Diagnostics.EventLog",
            "System.Diagnostics.FileVersionInfo",
            "System.Diagnostics.PerformanceCounter",
            "System.Diagnostics.Process",
            "System.Diagnostics.StackTrace",
            "System.Diagnostics.TextWriterTraceListener",
            "System.Diagnostics.Tools",
            "System.Diagnostics.TraceSource",
            "System.Drawing",
            "System.Drawing.Common",
            "System.Drawing.Design",
            "System.Drawing.Primitives",
            "System.Formats.Asn1",
            "System.Formats.Tar",
            "System.Globalization",
            "System.Globalization.Calendars",
            "System.Globalization.Extensions",
            "System.IO",
            "System.IO.Compression",
            "System.IO.Compression.Brotli",
            "System.IO.Compression.ZipFile",
            "System.IO.FileSystem",
            "System.IO.FileSystem.AccessControl",
            "System.IO.FileSystem.DriveInfo",
            "System.IO.FileSystem.Primitives",
            "System.IO.FileSystem.Watcher",
            "System.IO.IsolatedStorage",
            "System.IO.MemoryMappedFiles",
            "System.IO.Packaging",
            "System.IO.Pipes",
            "System.IO.Pipes.AccessControl",
            "System.IO.Ports",
            "System.IO.UnmanagedMemoryStream",
            "System.Linq",
            "System.Linq.Expressions",
            "System.Linq.Parallel",
            "System.Linq.Queryable",
            "System.Memory",
            "System.Memory.Data",
            "System.Net",
            "System.Net.Cache",
            "System.Net.Http",
            "System.Net.Http.Json",
            "System.Net.Http.WinHttpHandler",
            "System.Net.Mail",
            "System.Net.NameResolution",
            "System.Net.NetworkInformation",
            "System.Net.Ping",
            "System.Net.Primitives",
            "System.Net.Requests",
            "System.Net.Security",
            "System.Net.ServicePointManager",
            "System.Net.Sockets",
            "System.Net.WebClient",
            "System.Net.WebHeaderCollection",
            "System.Net.WebProxy",
            "System.Net.WebSockets",
            "System.Net.WebSockets.Client",
            "System.Net.WebSockets.Server",
            "System.Numerics",
            "System.Numerics.Vectors",
            "System.ObjectModel",
            "System.Reflection",
            "System.Reflection.Context",
            "System.Reflection.DispatchProxy",
            "System.Reflection.Emit",
            "System.Reflection.Emit.ILGeneration",
            "System.Reflection.Emit.Lightweight",
            "System.Reflection.Extensions",
            "System.Reflection.Metadata",
            "System.Reflection.MetadataLoadContext",
            "System.Reflection.Primitives",
            "System.Reflection.TypeExtensions",
            "System.Resources.Reader",
            "System.Resources.ResourceManager",
            "System.Resources.Writer",
            "System.Runtime",
            "System.Runtime.Caching",
            "System.Runtime.CompilerServices.Unsafe",
            "System.Runtime.CompilerServices.VisualC",
            "System.Runtime.Extensions",
            "System.Runtime.Handles",
            "System.Runtime.InteropServices",
            "System.Runtime.InteropServices.RuntimeInformation",
            "System.Runtime.InteropServices.WindowsRuntime",
            "System.Runtime.Intrinsics",
            "System.Runtime.Loader",
            "System.Runtime.Numerics",
            "System.Runtime.Serialization",
            "System.Runtime.Serialization.Formatters",
            "System.Runtime.Serialization.Json",
            "System.Runtime.Serialization.Primitives",
            "System.Runtime.Serialization.Xml",
            "System.Runtime.Serialization.XmlSerializers",
            "System.Security",
            "System.Security.AccessControl",
            "System.Security.Cryptography",
            "System.Security.Cryptography.Capi",
            "System.Security.Cryptography.Cng",
            "System.Security.Cryptography.OpenSsl",
            "System.Security.Cryptography.Pkcs",
            "System.Security.Cryptography.ProtectedData",
            "System.Security.Cryptography.Xml",
            "System.Security.Principal",
            "System.Security.Principal.Windows",
            "System.Security.SecureString",
            "System.ServiceModel.Syndication",
            "System.ServiceProcess",
            "System.ServiceProcess.ServiceController",
            "System.Text.Encoding",
            "System.Text.Encoding.CodePages",
            "System.Text.Encoding.Extensions",
            "System.Text.Encodings.Web",
            "System.Text.Json",
            "System.Text.Json.Serialization",
            "System.Text.Json.Serialization.Converters",
            "System.Text.RegularExpressions",
            "System.Threading",
            "System.Threading.Channels",
            "System.Threading.Overlapped",
            "System.Threading.Tasks",
            "System.Threading.Tasks.Dataflow",
            "System.Threading.Tasks.Extensions",
            "System.Threading.Tasks.Parallel",
            "System.Threading.Thread",
            "System.Threading.ThreadPool",
            "System.Threading.Timer",
            "System.Transactions",
            "System.Transactions.Local",
            "System.ValueTuple",
            "System.Web",
            "System.Web.HttpUtility",
            "System.Web.HttpUtility.JavaScript",
            "System.Windows",
            "System.Windows.Extensions",
            "System.Xml",
            "System.Xml.Linq",
            "System.Xml.ReaderWriter",
            "System.Xml.Serialization",
            "System.Xml.Serialization.Dynamic",
            "System.Xml.XDocument",
            "System.Xml.XmlDocument",
            "System.Xml.XmlSerializer",
            "System.Xml.XPath",
            "System.Xml.XPath.XDocument",
            "System.Xml.XPath.XmlDocument",
            "System.Xml.Xsl",
            "System.Xml.Xsl.Runtime",
            "WindowsBase",
        };

        return frameworkAssemblies.Contains(assemblyName);
    }

    /// <summary>
    /// Copies non-assembly module files (scripts, data, formats) to the output directory.
    /// </summary>
    private async Task<int> CopyModuleFilesAsync(
        ModulePackageInfo extractedInfo,
        string moduleOutputDir,
        string binDir,
        CancellationToken cancellationToken)
    {
        int copiedCount = 0;

        // Copy scripts
        if (extractedInfo.Scripts != null)
        {
            foreach (var script in extractedInfo.Scripts)
            {
                if (!File.Exists(script.FullPath))
                    continue;

                var manifestDir = Path.GetDirectoryName(extractedInfo.ManifestFile?.FullPath) ?? "";
                var relativePath = Path.GetRelativePath(manifestDir, script.FullPath);

                // Preserve directory structure relative to extraction root
                var destDir = Path.Combine(moduleOutputDir, Path.GetDirectoryName(relativePath) ?? "");
                Directory.CreateDirectory(destDir);

                var destPath = Path.Combine(destDir, Path.GetFileName(script.FullPath));

                try
                {
                    await using var sourceStream = File.OpenRead(script.FullPath);
                    await using var destStream = File.Create(destPath);
                    await sourceStream.CopyToAsync(destStream, cancellationToken);
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error copying script: {Path}", script.FullPath);
                }
            }
        }

        // Copy data files
        if (extractedInfo.DataFiles != null)
        {
            foreach (var dataFile in extractedInfo.DataFiles)
            {
                if (!File.Exists(dataFile.FullPath))
                    continue;

                var manifestDir = Path.GetDirectoryName(extractedInfo.ManifestFile?.FullPath) ?? "";
                var relativePath = Path.GetRelativePath(manifestDir, dataFile.FullPath);

                var destDir = Path.Combine(moduleOutputDir, Path.GetDirectoryName(relativePath) ?? "");
                Directory.CreateDirectory(destDir);

                var destPath = Path.Combine(destDir, Path.GetFileName(dataFile.FullPath));

                try
                {
                    await using var sourceStream = File.OpenRead(dataFile.FullPath);
                    await using var destStream = File.Create(destPath);
                    await sourceStream.CopyToAsync(destStream, cancellationToken);
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error copying data file: {Path}", dataFile.FullPath);
                }
            }
        }

        // Copy other files (formats, resources, etc.)
        if (extractedInfo.OtherFiles != null)
        {
            foreach (var otherFile in extractedInfo.OtherFiles)
            {
                if (!File.Exists(otherFile.FullPath))
                    continue;

                // Don't copy the manifest here; we'll create a new one
                var manifestFullPath = extractedInfo.ManifestFile?.FullPath;
                if (!string.IsNullOrEmpty(manifestFullPath) &&
                    otherFile.FullPath.Equals(manifestFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var manifestDir = Path.GetDirectoryName(extractedInfo.ManifestFile?.FullPath) ?? "";
                var relativePath = Path.GetRelativePath(manifestDir, otherFile.FullPath);

                var destDir = Path.Combine(moduleOutputDir, Path.GetDirectoryName(relativePath) ?? "");
                Directory.CreateDirectory(destDir);

                var destPath = Path.Combine(destDir, Path.GetFileName(otherFile.FullPath));

                try
                {
                    await using var sourceStream = File.OpenRead(otherFile.FullPath);
                    await using var destStream = File.Create(destPath);
                    await sourceStream.CopyToAsync(destStream, cancellationToken);
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error copying file: {Path}", otherFile.FullPath);
                }
            }
        }

        return copiedCount;
    }

    /// <summary>
    /// Determines which assemblies should be preloaded by the generic loader.
    /// Prioritizes system/framework assemblies that are often depended on early.
    /// </summary>
    private string[] DeterminePreloadAssemblies(IReadOnlyList<ModuleFile>? assemblies)
    {
        var preloadCandidates = new[] {
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "System.Text.Json.dll",
            "Newtonsoft.Json.dll",
            "System.Net.Http.dll",
            "Microsoft.Identity.Client.dll",
        };

        var result = new List<string>();

        if (assemblies != null)
        {
            foreach (var asm in assemblies)
            {
                var fileName = Path.GetFileName(asm.FullPath);
                if (preloadCandidates.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(fileName);
                }
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Well-known dependency libraries that frequently conflict between modules and must always be isolated,
    /// even when a module script happens to mention them (e.g. a net472-only LoadFrom call).
    /// </summary>
    private static readonly string[] AlwaysIsolatedPrefixes =
    {
        "Newtonsoft.Json",
        "System.Text.Json",
        "Microsoft.Identity.Client",
        "Microsoft.IdentityModel.",
        "System.IdentityModel.",
        "Microsoft.Extensions.",
        "Microsoft.Bcl.",
        "Azure.",
        "Microsoft.Rest.",
        "Microsoft.Graph.",
        "Polly",
    };

    /// <summary>
    /// Classifies every bundled assembly as either module-owned or an isolated dependency.
    /// An assembly is module-owned when a bundled script or manifest (.ps1/.psm1/.psd1/.ps1xml) refers to it by
    /// name (Import-Module, RequiredAssemblies, NestedModules, Add-Type, LoadFrom, type literals, ...);
    /// such assemblies must load into the Default ALC so PowerShell can resolve their types.
    /// Everything else is a dependency and is isolated in the private ALC.
    /// Explicit overrides (<paramref name="isolatePatterns"/> / <paramref name="sharedPatterns"/>, supporting
    /// PowerShell wildcards) take precedence over the heuristic; isolate wins when both match.
    /// </summary>
    private async Task<string[]> DetermineIsolatedAssembliesAsync(
        string moduleOutputDir,
        string[]? isolatePatterns,
        string[]? sharedPatterns,
        string loaderAssemblyName,
        CancellationToken cancellationToken)
    {
        var isolateOverrides = CreateWildcardPatterns(isolatePatterns);
        var sharedOverrides = CreateWildcardPatterns(sharedPatterns);

        var scriptExtensions = new[] { ".ps1", ".psm1", ".psd1", ".ps1xml" };
        var scriptText = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(moduleOutputDir, "*", SearchOption.AllDirectories))
        {
            if (!scriptExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

            scriptText.Append(await File.ReadAllTextAsync(file, cancellationToken)).Append('\n');
        }
        var allScriptText = scriptText.ToString();

        var isolated = new List<string>();
        var owned = new List<string>();
        var assemblyNames = Directory.EnumerateFiles(moduleOutputDir, "*.dll", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        foreach (var name in assemblyNames)
        {
            if (name!.Equals("Svrooij.PowerShellRepackager.GenericLoader", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(loaderAssemblyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isolateOverrides.Any(p => p.IsMatch(name)))
            {
                isolated.Add(name);
                continue;
            }

            if (sharedOverrides.Any(p => p.IsMatch(name)))
            {
                owned.Add(name);
                continue;
            }

            var alwaysIsolate = AlwaysIsolatedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            var referencedByScript = allScriptText.Contains(name, StringComparison.OrdinalIgnoreCase);

            if (!alwaysIsolate && referencedByScript)
                owned.Add(name);
            else
                isolated.Add(name);
        }

        _logger.LogInformation("Module-owned assemblies (Default ALC): {Owned}", string.Join(", ", owned));
        _logger.LogInformation("Isolated dependency assemblies (private ALC): {Count}", isolated.Count);

        return isolated.ToArray();
    }

    private static WildcardPattern[] CreateWildcardPatterns(string[]? patterns)
        => (patterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new WildcardPattern(
                p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p,
                WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant))
            .ToArray();

    /// <summary>
    /// Generates the loader configuration file (loader-config.json).
    /// </summary>
    private async Task<string> GenerateLoaderConfigAsync(
        string binDir,
        string moduleName,
        string[] preloadAssemblies,
        string[] isolatedAssemblies,
        CancellationToken cancellationToken)
    {
        // Scan the module directory to find all folders containing assemblies
        var moduleDir = Path.GetDirectoryName(binDir) ?? binDir;
        var assemblySearchPaths = new List<string> { "bin" }; // Always include bin as fallback
        var runtimesPaths = new List<string>();

        try
        {
            // Look for folders containing .dll files (relative to module root)
            var allDirs = Directory.GetDirectories(moduleDir, "*", SearchOption.AllDirectories);
            foreach (var dir in allDirs)
            {
                var relativePath = Path.GetRelativePath(moduleDir, dir);
                var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // NuGet-style RID folders (runtimes/{rid}/native, runtimes/{rid}/lib/{tfm}) are resolved at
                // import time by the loader for the current OS/architecture; only record the runtimes root.
                var runtimesIndex = Array.FindIndex(segments, s => s.Equals("runtimes", StringComparison.OrdinalIgnoreCase));
                if (runtimesIndex >= 0)
                {
                    var runtimesRoot = string.Join('/', segments.Take(runtimesIndex + 1));
                    if (!runtimesPaths.Contains(runtimesRoot, StringComparer.OrdinalIgnoreCase))
                    {
                        runtimesPaths.Add(runtimesRoot);
                    }
                    continue;
                }

                // ref/ folders contain reference assemblies (metadata only) that must never be loaded
                if (segments.Any(s => s.Equals("ref", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var dllFiles = Directory.GetFiles(dir, "*.dll");
                if (dllFiles.Length > 0)
                {
                    var searchPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                    if (!assemblySearchPaths.Contains(searchPath, StringComparer.OrdinalIgnoreCase))
                    {
                        assemblySearchPaths.Add(searchPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning for assembly search paths, using default bin/");
        }

        _logger.LogInformation("Assembly search paths: {Paths}", string.Join(", ", assemblySearchPaths));
        if (runtimesPaths.Count > 0)
        {
            _logger.LogInformation("RID-specific runtimes folders (resolved at import time): {Paths}", string.Join(", ", runtimesPaths));
        }

        var loaderConfig = new
        {
            moduleName = moduleName,
            preloadAssemblies = preloadAssemblies,
            assemblySearchPaths = assemblySearchPaths.ToArray(),
            runtimesPaths = runtimesPaths.ToArray(),
            isolatedAssemblies = isolatedAssemblies
        };

        var configPath = Path.Combine(binDir, "loader-config.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(loaderConfig, jsonOptions);

        await File.WriteAllTextAsync(configPath, json, cancellationToken);
        _logger.LogDebug("Generated loader config: {Path}", configPath);

        return configPath;
    }

    /// <summary>
    /// Generates a .psm1 wrapper script for modules that originally used a .psm1 RootModule.
    /// The wrapper initializes the generic loader and then dot-sources the original .psm1
    /// so all its orchestration logic (conditional imports, relative path references, etc.) runs
    /// within the isolated AssemblyLoadContext.
    /// For binary-module-based packages, returns null (no wrapper needed).
    /// </summary>
    private async Task<string?> GeneratePsm1WrapperAsync(
        ManifestData manifestData,
        string moduleOutputDir,
        string repackagedModuleName,
        CancellationToken cancellationToken)
    {
        // Only generate a wrapper if the original module used a .psm1 RootModule
        if (manifestData.IsBinaryModule || string.IsNullOrEmpty(manifestData.RootModule) ||
            !manifestData.RootModule.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping .psm1 wrapper generation: original module uses binary RootModule or has no RootModule");
            return null;
        }

        _logger.LogInformation("Generating .psm1 wrapper for module with original RootModule: {RootModule}",
            manifestData.RootModule);

        try
        {
            // Dot-sourcing a .psm1 gives it its own module scope, so its Import-Module calls would never
            // surface as exports of the wrapper. Copy the original root module to a .ps1 (executed in the
            // caller's scope when dot-sourced) and dot-source that instead.
            var originalRootModule = manifestData.RootModule.Replace('\\', '/').TrimStart('.', '/');
            var originalRootPath = Path.Combine(moduleOutputDir, originalRootModule.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(originalRootPath))
            {
                _logger.LogError("Original RootModule not found in output: {Path}", originalRootPath);
                return null;
            }

            var dotSourceScript = Path.ChangeExtension(originalRootModule, ".original.ps1");
            var dotSourcePath = Path.Combine(moduleOutputDir, dotSourceScript.Replace('/', Path.DirectorySeparatorChar));
            var originalContent = await File.ReadAllTextAsync(originalRootPath, cancellationToken);
            var scriptContent =
                $"# Copy of the original root module '{originalRootModule}', renamed to .ps1 so it can be dot-sourced\r\n" +
                $"# into the repackaged module scope by {repackagedModuleName}.psm1. Signature block removed.\r\n" +
                StripScriptSignature(originalContent);
            await File.WriteAllTextAsync(dotSourcePath, scriptContent, Encoding.UTF8, cancellationToken);
            _logger.LogDebug("Copied original RootModule to dot-source script: {Path}", dotSourcePath);

            var template = await GetPsm1TemplateAsync(dotSourceScript, cancellationToken);

            // Write the wrapper to the output directory with the repackaged module name
            var wrapperPath = Path.Combine(moduleOutputDir, $"{repackagedModuleName}.psm1");

            await File.WriteAllTextAsync(wrapperPath, template, Encoding.UTF8, cancellationToken);
            _logger.LogDebug("Generated .psm1 wrapper: {Path}, will dot-source: {Script}", wrapperPath, dotSourceScript);

            return wrapperPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating .psm1 wrapper");
            return null;
        }
    }

    /// <summary>
    /// Removes an Authenticode signature block (<c># SIG # Begin signature block</c> ... <c># SIG # End signature block</c>)
    /// from a script. The signature no longer applies once the file is copied/renamed.
    /// </summary>
    private static string StripScriptSignature(string scriptContent)
    {
        var index = scriptContent.IndexOf("# SIG # Begin signature block", StringComparison.OrdinalIgnoreCase);
        return index == -1 ? scriptContent : scriptContent.Substring(0, index).TrimEnd() + "\r\n";
    }

    /// <summary>
    /// Generates the .psm1 wrapper template that orchestrates:
    /// 1. The generic loader DLL initialization (already done via NestedModule import)
    /// 2. Dot-sourcing the .ps1 copy of the original root module to preserve all orchestration logic
    /// The original .psm1 will execute with relative paths intact and assemblies resolved
    /// through the isolated AssemblyLoadContext.
    /// </summary>
    private async Task<string> GetPsm1TemplateAsync(string originalRootModule, CancellationToken cancellationToken)
    {
        // Template content - initializes loader then dot-sources the original .psm1
        return $$"""
# Repackaged module wrapper - orchestrates loader and original module logic
# This wrapper ensures the generic loader's AssemblyLoadContext is initialized before
# the original .psm1 runs, so all its imports and assembly loads are resolved correctly.

$ModuleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Debug "Repackaged module wrapper starting. Module root: $ModuleRoot"

# The generic loader (NestedModule) has been imported before this script runs.
# It has initialized the AssemblyLoadContext and registered the assembly resolver.
# Now dot-source the original module so all its orchestration logic runs in this context.

$OriginalModule = Join-Path $ModuleRoot '{{originalRootModule}}'

if (-not (Test-Path $OriginalModule)) {
    Write-Error "Original module script not found at: $OriginalModule"
    throw "Failed to locate original module: {{originalRootModule}}"
}

Write-Debug "Dot-sourcing original module: $OriginalModule"

try {
    . $OriginalModule
    Write-Debug "Successfully dot-sourced original module"
}
catch {
    Write-Error "Error dot-sourcing original module: $_"
    throw
}
""";
    }

    /// <summary>
    /// Rewrites the PowerShell manifest with the new module name, GUID, and loader entry point.
    /// Removes ModuleName from the manifest as it's not needed with the generic loader.
    /// Preserves all export lists (CmdletsToExport, FunctionsToExport, etc.) from the original manifest.
    /// Sets the original module's main DLL as RootModule and generic loader as NestedModule,
    /// OR sets the generated .psm1 wrapper as RootModule for script-based modules.
    /// </summary>
    private async Task<string> RewriteManifestAsync(
        string? originalManifestPath,
        ManifestData manifestData,
        string moduleOutputDir,
        string newModuleName,
        Guid newGuid,
        string packageVersion,
        string loaderFileName,
        CancellationToken cancellationToken)
    {
        var manifestContent = (string.IsNullOrEmpty(originalManifestPath) || !File.Exists(originalManifestPath))
            ? GetDefaultManifestTemplate(newModuleName, newGuid, loaderFileName)
            : await File.ReadAllTextAsync(originalManifestPath, cancellationToken);

        // The repackaged module is Core-only: collapse any `if ($PSEdition -eq 'Core') {...} else {...}` values
        manifestContent = ResolvePSEditionConditionals(manifestContent);

        // FileList refers to the original layout (and the repackaged module adds loader files); drop it
        manifestContent = RemoveManifestValue(manifestContent, "FileList");

        // Note: never strip 'ModuleName' lines here; a manifest has no root ModuleName key, the only
        // occurrences are inside RequiredModules/NestedModules specs where they are mandatory.

        // Update key fields for the repackaged module
        manifestContent = UpdateManifestField(manifestContent, "GUID", newGuid.ToString());
        manifestContent = UpdateManifestField(manifestContent, "ModuleVersion", packageVersion);
        manifestContent = UpdateManifestField(manifestContent, "PowerShellVersion", "7.4");

        // Repackaged modules run on PowerShell 7+ only: declare Core edition and drop Desktop-only constraints
        manifestContent = UpdateManifestField(manifestContent, "CompatiblePSEditions", "@('Core')");
        manifestContent = RemoveManifestField(manifestContent, "DotNetFrameworkVersion");
        manifestContent = RemoveManifestField(manifestContent, "CLRVersion");
        manifestContent = RemoveManifestField(manifestContent, "ProcessorArchitecture");
        manifestContent = RemoveManifestField(manifestContent, "PowerShellHostName");
        manifestContent = RemoveManifestField(manifestContent, "PowerShellHostVersion");

        // Attribution: this package is published by the repackager, not the original vendor
        manifestContent = RewriteAttribution(manifestContent, manifestData, newModuleName);

        // Handle RootModule
        if (manifestData.IsBinaryModule)
        {
            // For binary modules: use the original DLL as RootModule, and generic loader as NestedModule
            var rootModuleDll = DetermineRootModuleDll(manifestData);
            manifestContent = UpdateManifestField(manifestContent, "RootModule", $"bin/{rootModuleDll}");
            manifestContent = UpdateManifestField(manifestContent, "NestedModules", $"@('bin/{loaderFileName}')");
        }
        else
        {
            // For script-based modules: generate a .psm1 wrapper and use it as RootModule
            var wrapperPath = await GeneratePsm1WrapperAsync(manifestData, moduleOutputDir, newModuleName, cancellationToken);

            if (wrapperPath != null)
            {
                // Use the generated .psm1 wrapper as RootModule
                manifestContent = UpdateManifestField(manifestContent, "RootModule", $"{newModuleName}.psm1");
                // Generic loader still goes in NestedModules so it's initialized before the wrapper runs
                manifestContent = UpdateManifestField(manifestContent, "NestedModules", $"@('bin/{loaderFileName}')");
                _logger.LogInformation("Set RootModule to generated .psm1 wrapper: {ModuleName}.psm1", newModuleName);
            }
            else
            {
                // Fallback: use a default DLL-based approach if wrapper generation failed
                _logger.LogWarning("Failed to generate .psm1 wrapper, falling back to DLL-based RootModule");
                var fallbackDll = DetermineRootModuleDll(manifestData);
                manifestContent = UpdateManifestField(manifestContent, "RootModule", $"bin/{fallbackDll}");
                manifestContent = UpdateManifestField(manifestContent, "NestedModules", $"@('bin/{loaderFileName}')");
            }
        }

        // Important: Ensure export lists are present for cmdlet discovery
        // Even if they were empty in the original, PowerShell needs them to be explicit
        EnsureExportLists(ref manifestContent);

        // Log the CmdletsToExport field for debugging
        var cmdletsMatch = System.Text.RegularExpressions.Regex.Match(
            manifestContent, "CmdletsToExport\\s*=\\s*([^\r\n]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (cmdletsMatch.Success)
        {
            _logger.LogInformation("Repackaged manifest CmdletsToExport: {CmdletsValue}", cmdletsMatch.Groups[1].Value.Trim());
        }

        // Strip any signatures (digital signatures from original module)
        manifestContent = StripSignature(manifestContent);

        // Add repackaging metadata
        manifestContent = AddRepackagingMetadata(manifestContent, manifestData);

        var rewrittenPath = Path.Combine(moduleOutputDir, $"{newModuleName}.psd1");
        await File.WriteAllTextAsync(rewrittenPath, manifestContent, Encoding.UTF8, cancellationToken);
        _logger.LogDebug("Wrote rewritten manifest: {Path}", rewrittenPath);

        return rewrittenPath;
    }

    /// <summary>
    /// Determines the original module's main DLL file to use as RootModule.
    /// This is typically the DLL that has the same name as the module or the one declared as RootModule.
    /// </summary>
    private string DetermineRootModuleDll(ManifestData manifestData)
    {
        // If the original manifest specified a RootModule that's a DLL, use its filename
        if (!string.IsNullOrEmpty(manifestData.RootModule) &&
            manifestData.RootModule.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dllName = Path.GetFileName(manifestData.RootModule);
            _logger.LogInformation("Using original RootModule DLL: {DllName}", dllName);
            return dllName;
        }

        // Otherwise, try to infer from the module name (e.g., "MicrosoftTeams" → "MicrosoftTeams.dll")
        if (!string.IsNullOrEmpty(manifestData.ModuleName))
        {
            var inferredDll = $"{manifestData.ModuleName}.dll";
            _logger.LogInformation("Inferred RootModule DLL from module name: {DllName}", inferredDll);
            return inferredDll;
        }

        // Fallback - this shouldn't normally happen
        _logger.LogWarning("Could not determine RootModule DLL, defaulting to 'Module.dll'");
        return "Module.dll";
    }

    /// <summary>
    /// Ensures that export lists exist in the manifest. If they don't exist, adds empty arrays.
    /// This is necessary for PowerShell to properly discover cmdlets from binary modules loaded through the generic loader.
    /// </summary>
    private void EnsureExportLists(ref string manifestContent)
    {
        var exportFields = new[]
        {
            "CmdletsToExport",
            "FunctionsToExport",
            "AliasesToExport",
            "VariablesToExport"
        };

        foreach (var field in exportFields)
        {
            // Check if the field exists (ignoring commented-out lines)
            var pattern = $"^(?!\\s*#)\\s*{field}\\s*=";
            var regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);

            if (!regex.IsMatch(manifestContent))
            {
                // Field doesn't exist, add it as an empty array
                _logger.LogWarning("Export field {Field} not found in manifest - adding empty array", field);
                manifestContent = InsertAtRootHashtable(manifestContent, $"    {field} = @()");
                _logger.LogDebug("Added empty {Field} to manifest for cmdlet discovery", field);
            }
            else
            {
                // Log that field was found
                var fieldMatch = System.Text.RegularExpressions.Regex.Match(
                    manifestContent, $"{field}\\s*=\\s*([^\r\n]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fieldMatch.Success)
                {
                    _logger.LogDebug("Found {Field} in manifest: {Value}", field, fieldMatch.Groups[1].Value.Trim());
                }
            }
        }
    }

    /// <summary>
    /// Updates or adds a manifest field value. Handles single-line values and preserves multiline arrays.
    /// For values that start with @ (arrays), don't add quotes. For scalar values, add quotes.
    /// Uses word boundaries to avoid matching partial field names (e.g., "NestedModules" won't match part of "VariablesToExport").
    /// </summary>
    private string UpdateManifestField(string manifestContent, string fieldName, string value)
    {
        // Determine if value should be quoted (array literals starting with @ should not be quoted)
        var isArrayLiteral = value.StartsWith("@");
        var quotedValue = isArrayLiteral ? value : $"'{value}'";

        // Pattern for exact field match with word boundaries
        // Matches: FieldName = value (handling indentation)
        // Does NOT match: SomeFieldNameSuffix or PrefixFieldName, nor commented-out lines (# FieldName = ...)
        var singleLinePattern = $"(?m)^(?!\\s*#)\\s*{System.Text.RegularExpressions.Regex.Escape(fieldName)}\\s*=\\s*[^\r\n]*";
        var singleLineReplacement = $"    {fieldName} = {quotedValue}";

        // Check if this field exists at the start of a line (at root hashtable level, not nested)
        var regex = new System.Text.RegularExpressions.Regex(
            singleLinePattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        var result = regex.Replace(manifestContent, singleLineReplacement, 1);

        // If field wasn't found, add it to the beginning of the root hashtable
        if (result == manifestContent)
        {
            result = InsertAtRootHashtable(manifestContent, $"    {fieldName} = {quotedValue}");
        }

        return result;
    }

    /// <summary>
    /// Inserts a line directly after the opening <c>@{</c> of the root hashtable.
    /// Only the first <c>@{</c> is touched so nested hashtables (e.g. PrivateData/PSData) are left intact.
    /// </summary>
    private static string InsertAtRootHashtable(string manifestContent, string line)
    {
        var index = manifestContent.IndexOf("@{", StringComparison.Ordinal);
        if (index == -1)
        {
            return manifestContent;
        }

        return manifestContent.Insert(index + 2, $"\r\n{line}");
    }

    /// <summary>
    /// Removes a manifest field entirely from the manifest content.
    /// Uses exact field name matching to avoid removing partial matches.
    /// </summary>
    private string RemoveManifestField(string manifestContent, string fieldName)
    {
        // Pattern to match the exact field line at the start of a line (with leading whitespace)
        // Removes the entire line including newline
        var pattern = $"(?m)^(?!\\s*#)\\s*{System.Text.RegularExpressions.Regex.Escape(fieldName)}\\s*=\\s*[^\r\n]*\\r?\\n?";

        var result = System.Text.RegularExpressions.Regex.Replace(
            manifestContent, pattern, "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);

        return result;
    }

    /// <summary>
    /// Strips digital signatures from the manifest file.
    /// PowerShell manifest signatures are appended after the closing brace and are not valid in repackaged modules.
    /// </summary>
    private string StripSignature(string manifestContent)
    {
        // Find the last closing brace of the hashtable
        var lastBraceIndex = manifestContent.LastIndexOf('}');

        if (lastBraceIndex == -1)
            return manifestContent;

        // Remove everything after the closing brace (which contains the signature)
        return manifestContent.Substring(0, lastBraceIndex + 1);
    }

    /// <summary>
    /// Collapses <c>if ($PSEdition -eq 'Core') { A } else { B }</c> (and the <c>-ne 'Desktop'</c> / swapped variants)
    /// values inside the manifest to just the Core branch. Some modules (e.g. ExchangeOnlineManagement) use this for
    /// RootModule/FileList; since the repackaged module is Core-only the conditional is redundant and the simple
    /// value is required for restricted-language manifest parsing.
    /// </summary>
    private string ResolvePSEditionConditionals(string manifestContent)
    {
        var ast = Parser.ParseInput(manifestContent, out _, out _);
        var ifStatements = ast.FindAll(a => a is IfStatementAst, searchNestedScriptBlocks: true)
            .Cast<IfStatementAst>()
            .Where(i => i.Clauses.Count == 1)
            .OrderByDescending(i => i.Extent.StartOffset)
            .ToList();

        if (ifStatements.Count == 0)
            return manifestContent;

        var sb = new StringBuilder(manifestContent);
        foreach (var ifStatement in ifStatements)
        {
            var (condition, ifBody) = ifStatement.Clauses[0];
            var coreIsIfBranch = IsPSEditionCoreCondition(condition);
            if (coreIsIfBranch == null)
            {
                _logger.LogWarning("Manifest contains an unrecognized conditional value, leaving as-is: {Condition}", condition.Extent.Text);
                continue;
            }

            StatementBlockAst? selected = coreIsIfBranch.Value ? ifBody : ifStatement.ElseClause;
            if (selected == null)
            {
                _logger.LogWarning("Manifest conditional has no Core branch, leaving as-is: {Condition}", condition.Extent.Text);
                continue;
            }

            // Take the inner statements without the surrounding braces
            var inner = selected.Extent.Text.Trim();
            if (inner.StartsWith('{') && inner.EndsWith('}'))
                inner = inner[1..^1].Trim();

            sb.Remove(ifStatement.Extent.StartOffset, ifStatement.Extent.EndOffset - ifStatement.Extent.StartOffset);
            sb.Insert(ifStatement.Extent.StartOffset, inner);
            _logger.LogInformation("Resolved $PSEdition conditional in manifest to Core value: {Value}",
                inner.Length > 80 ? inner[..80] + "..." : inner);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the condition selects Core in its "if" branch, false if it selects Desktop
    /// (so Core is the else branch), or null if the condition is not a recognizable $PSEdition test.
    /// </summary>
    private static bool? IsPSEditionCoreCondition(PipelineBaseAst condition)
    {
        var binary = condition.Find(a => a is BinaryExpressionAst, searchNestedScriptBlocks: false) as BinaryExpressionAst;
        if (binary == null)
            return null;

        static bool IsPSEditionVar(ExpressionAst e) =>
            e is VariableExpressionAst v && v.VariablePath.UserPath.Equals("PSEdition", StringComparison.OrdinalIgnoreCase);

        static string? EditionLiteral(ExpressionAst e) =>
            e is StringConstantExpressionAst s ? s.Value : null;

        string? edition = null;
        if (IsPSEditionVar(binary.Left)) edition = EditionLiteral(binary.Right);
        else if (IsPSEditionVar(binary.Right)) edition = EditionLiteral(binary.Left);
        if (edition == null)
            return null;

        var isEquals = binary.Operator is TokenKind.Ieq or TokenKind.Ceq;
        var isNotEquals = binary.Operator is TokenKind.Ine or TokenKind.Cne;
        if (!isEquals && !isNotEquals)
            return null;

        var literalIsCore = edition.Equals("Core", StringComparison.OrdinalIgnoreCase);
        var literalIsDesktop = edition.Equals("Desktop", StringComparison.OrdinalIgnoreCase);
        if (!literalIsCore && !literalIsDesktop)
            return null;

        // if ($PSEdition -eq 'Core') / if ($PSEdition -ne 'Desktop') → if-branch is Core
        return literalIsCore == isEquals;
    }

    /// <summary>
    /// Removes a root-level manifest entry regardless of the shape of its value (single line, multi-line array,
    /// hashtable or conditional), using the AST to find the exact extent.
    /// </summary>
    private string RemoveManifestValue(string manifestContent, string fieldName)
    {
        var ast = Parser.ParseInput(manifestContent, out _, out _);
        var root = ast.Find(a => a is HashtableAst, searchNestedScriptBlocks: false) as HashtableAst;
        if (root == null)
            return manifestContent;

        var entry = root.KeyValuePairs.FirstOrDefault(kv =>
            kv.Item1 is StringConstantExpressionAst key &&
            key.Value.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return manifestContent;

        var start = entry.Item1.Extent.StartOffset;
        var end = entry.Item2.Extent.EndOffset;

        // Also remove the leading indentation and trailing newline so no blank line is left behind
        while (start > 0 && (manifestContent[start - 1] == ' ' || manifestContent[start - 1] == '\t'))
            start--;
        if (end < manifestContent.Length && manifestContent[end] == '\r') end++;
        if (end < manifestContent.Length && manifestContent[end] == '\n') end++;

        _logger.LogDebug("Removed manifest entry {Field}", fieldName);
        return manifestContent.Remove(start, end - start);
    }

    /// <summary>
    /// Adds metadata comments about the repackaging operation to the manifest.
    /// </summary>
    private string AddRepackagingMetadata(string manifestContent, ManifestData originalData)
    {
        var metadata = new StringBuilder();
        metadata.AppendLine("# Repackaging Information:");
        metadata.AppendLine($"# Original Module: {originalData.ModuleName} v{originalData.ModuleVersion}");
        metadata.AppendLine($"# Original GUID: {originalData.ModuleGuid}");
        metadata.AppendLine($"# Original Author: {originalData.Author ?? "Unknown"}");
        metadata.AppendLine($"# Original Package: {GetOriginalGalleryUrl(originalData)}");
        metadata.AppendLine($"# Repackaged: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        metadata.AppendLine($"# Repackaged with: {RepackagerProjectUrl}");
        metadata.AppendLine("#");

        return metadata.ToString() + manifestContent;
    }

    private const string RepackagerProjectUrl = "https://github.com/svrooij/PowerShellRepackager";
    private const string RepackagedModulesDocUrl = RepackagerProjectUrl + "/blob/main/docs/REPACKAGED-MODULES.md";
    private const string RepackagedAuthor = "Stephan van Rooij";
    private const string RepackagedCompany = "Svrooij";

    private static string GetOriginalGalleryUrl(ManifestData data) =>
        $"https://www.powershellgallery.com/packages/{data.ModuleName}/{data.ModuleVersion}";

    private static string GetModuleDocUrl(ManifestData data) =>
        $"{RepackagerProjectUrl}/blob/main/docs/modules/{data.ModuleName}.md";

    /// <summary>
    /// Rewrites Author/CompanyName/Description and PSData URIs so the published package is clearly
    /// attributed to the repackager while pointing back to the original module and its license.
    /// </summary>
    private string RewriteAttribution(string manifestContent, ManifestData data, string newModuleName)
    {
        var originalAuthor = data.Author ?? "Unknown";
        var originalDescription = (data.Description ?? string.Empty).Replace("'", "''");
        var description =
            $"Repackaged version of {data.ModuleName} {data.ModuleVersion} by {originalAuthor.Replace("'", "''")}, " +
            $"with isolated assembly loading for PowerShell 7.4+. Not affiliated with the original author. " +
            $"Original package: {GetOriginalGalleryUrl(data)}. Attribution and license details: {GetModuleDocUrl(data)}" +
            (originalDescription.Length > 0 ? $" Original description: {originalDescription}" : string.Empty);

        manifestContent = UpdateManifestField(manifestContent, "Author", RepackagedAuthor);
        manifestContent = UpdateManifestField(manifestContent, "CompanyName", RepackagedCompany);
        manifestContent = RemoveQuotedScalarField(manifestContent, "Description");
        manifestContent = UpdateManifestField(manifestContent, "Description", description);
        manifestContent = RemoveQuotedScalarField(manifestContent, "Copyright");
        manifestContent = UpdateManifestField(manifestContent, "Copyright",
            $"(c) {DateTime.UtcNow.Year} {RepackagedAuthor} (repackaging). Original module: {(data.Copyright ?? originalAuthor).Replace("'", "''")}");

        // HelpInfoURI and IconUri belong to the original module identity/brand
        manifestContent = RemoveManifestField(manifestContent, "HelpInfoURI");
        manifestContent = RemoveManifestField(manifestContent, "IconUri");

        // PSData URIs: project points to our attribution page; license stays with the original (it governs redistribution)
        manifestContent = UpdateNestedManifestField(manifestContent, "ProjectUri", GetModuleDocUrl(data));
        if (!string.IsNullOrEmpty(data.LicenseUri))
        {
            manifestContent = UpdateNestedManifestField(manifestContent, "LicenseUri", data.LicenseUri);
        }

        var releaseNotes = $"Repackaged from {data.ModuleName} {data.ModuleVersion} using PowerShellRepackager ({RepackagerProjectUrl}). See {GetModuleDocUrl(data)} for details.";
        manifestContent = UpdateNestedManifestField(manifestContent, "ReleaseNotes", releaseNotes);

        var tags = new List<string> { "Repackaged", "PSEdition_Core" };
        tags.AddRange((data.Tags ?? [])
            .Where(t => !t.Equals("PSEdition_Desktop", StringComparison.OrdinalIgnoreCase))
            .Where(t => !tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
        var tagsLiteral = "@(" + string.Join(", ", tags.Select(t => $"'{t.Replace("'", "''")}'")) + ")";
        manifestContent = UpdateNestedManifestField(manifestContent, "Tags", tagsLiteral);

        return manifestContent;
    }

    /// <summary>
    /// Removes a root-level field whose value is a (possibly multi-line) single- or double-quoted string.
    /// </summary>
    private static string RemoveQuotedScalarField(string manifestContent, string fieldName)
    {
        var pattern = $@"(?m)^(?!\s*#)[ \t]*{System.Text.RegularExpressions.Regex.Escape(fieldName)}[ \t]*=[ \t]*(?:@'\r?\n[\s\S]*?\r?\n'@|@""\r?\n[\s\S]*?\r?\n""@|'(?:[^']|'')*'|""(?:[^""]|"""")*"")[ \t]*\r?\n?";
        return System.Text.RegularExpressions.Regex.Replace(manifestContent, pattern, string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Updates a PSData field wherever it is in the manifest (any nesting level).
    /// it is inserted into the PSData hashtable, creating PrivateData/PSData when needed.
    /// </summary>
    private static string UpdateNestedManifestField(string manifestContent, string fieldName, string value)
    {
        var isArrayLiteral = value.StartsWith("@");
        var quotedValue = isArrayLiteral ? value : $"'{value}'";

        // Existing value: here-string, quoted string (possibly multi-line) optionally followed by a bare comma-list
        // of more quoted strings on the same line (e.g. Tags = 'A', 'B'), array literal, or single line
        var quoted = @"(?:'(?:[^']|'')*'|""(?:[^""]|"""")*"")";
        var pattern = $@"(?m)^(?!\s*#)(?<indent>[ \t]*){System.Text.RegularExpressions.Regex.Escape(fieldName)}[ \t]*=[ \t]*(?<value>@'\r?\n[\s\S]*?\r?\n'@|@""\r?\n[\s\S]*?\r?\n""@|{quoted}(?:[ \t]*,[ \t]*{quoted})*|@\([\s\S]*?\)|[^\r\n]*)";
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (regex.IsMatch(manifestContent))
        {
            return regex.Replace(manifestContent, m => $"{m.Groups["indent"].Value}{fieldName} = {quotedValue}", 1);
        }

        // Insert into PSData = @{
        var psDataMatch = System.Text.RegularExpressions.Regex.Match(manifestContent, @"(?m)^(?!\s*#)\s*PSData\s*=\s*@\{", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (psDataMatch.Success)
        {
            return manifestContent.Insert(psDataMatch.Index + psDataMatch.Length, $"\r\n            {fieldName} = {quotedValue}");
        }

        // Insert into PrivateData = @{ (creating PSData)
        var privateDataMatch = System.Text.RegularExpressions.Regex.Match(manifestContent, @"(?m)^(?!\s*#)\s*PrivateData\s*=\s*@\{", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (privateDataMatch.Success)
        {
            return manifestContent.Insert(privateDataMatch.Index + privateDataMatch.Length,
                $"\r\n        PSData = @{{\r\n            {fieldName} = {quotedValue}\r\n        }}");
        }

        // No PrivateData at all: create it at the root
        return InsertAtRootHashtable(manifestContent,
            $"    PrivateData = @{{\r\n        PSData = @{{\r\n            {fieldName} = {quotedValue}\r\n        }}\r\n    }}");
    }

    /// <summary>
    /// Writes README.md with attribution to the original module into the repackaged module folder and copies
    /// the original license file (when present in the package) as LICENSE.original.txt.
    /// </summary>
    private async Task WriteAttributionAsync(
        ModulePackageInfo extractedInfo,
        ManifestData data,
        string moduleOutputDir,
        string newModuleName,
        CancellationToken cancellationToken)
    {
        string? bundledLicense = null;
        var extractedRoot = extractedInfo.ManifestFile?.FullPath is { } manifest ? Path.GetDirectoryName(manifest) : null;
        if (extractedRoot != null && Directory.Exists(extractedRoot))
        {
            var licenseFile = Directory.EnumerateFiles(extractedRoot, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("LICENSE", StringComparison.OrdinalIgnoreCase)
                                  || Path.GetFileNameWithoutExtension(f).Equals("LICENSE.original", StringComparison.OrdinalIgnoreCase)
                                  || Path.GetFileName(f).Equals("license.txt", StringComparison.OrdinalIgnoreCase));
            if (licenseFile != null)
            {
                bundledLicense = "LICENSE.original.txt";
                File.Copy(licenseFile, Path.Combine(moduleOutputDir, bundledLicense), overwrite: true);
                _logger.LogInformation("Copied original license to {File}", bundledLicense);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {newModuleName}");
        sb.AppendLine();
        sb.AppendLine($"This is a **repackaged** version of the PowerShell module **{data.ModuleName} {data.ModuleVersion}**.");
        sb.AppendLine("It is **not** produced, endorsed, or supported by the original author. The only changes are the");
        sb.AppendLine("module identity (name/GUID), a rewritten manifest, and a generic assembly loader that isolates the");
        sb.AppendLine("module's dependencies in a private `AssemblyLoadContext` so it can coexist with other modules in PowerShell 7.4+.");
        sb.AppendLine();
        sb.AppendLine("## Original module");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Name | `{data.ModuleName}` |");
        sb.AppendLine($"| Version | `{data.ModuleVersion}` |");
        sb.AppendLine($"| Author | {data.Author ?? "Unknown"} |");
        if (!string.IsNullOrEmpty(data.CompanyName)) sb.AppendLine($"| Company | {data.CompanyName} |");
        if (!string.IsNullOrEmpty(data.Copyright)) sb.AppendLine($"| Copyright | {data.Copyright} |");
        sb.AppendLine($"| PowerShell Gallery | {GetOriginalGalleryUrl(data)} |");
        if (!string.IsNullOrEmpty(data.ProjectUri)) sb.AppendLine($"| Project | {data.ProjectUri} |");
        if (!string.IsNullOrEmpty(data.LicenseUri)) sb.AppendLine($"| License | {data.LicenseUri} |");
        if (bundledLicense != null) sb.AppendLine($"| Bundled license | [`{bundledLicense}`](./{bundledLicense}) |");
        if (data.ModuleGuid.HasValue) sb.AppendLine($"| GUID | `{data.ModuleGuid}` |");
        sb.AppendLine();
        sb.AppendLine("## License");
        sb.AppendLine();
        sb.AppendLine("The original module's license continues to apply to all original files (binaries, scripts, help, formats).");
        sb.AppendLine("The repackaging tooling and the generic loader are licensed under the PowerShellRepackager repository license.");
        sb.AppendLine();
        sb.AppendLine("## Repackaging");
        sb.AppendLine();
        sb.AppendLine($"- Tool: [PowerShellRepackager]({RepackagerProjectUrl})");
        sb.AppendLine($"- Overview of repackaged modules: {RepackagedModulesDocUrl}");
        sb.AppendLine($"- Module page: {GetModuleDocUrl(data)}");
        sb.AppendLine($"- Repackaged on: {DateTime.UtcNow:yyyy-MM-dd} UTC");
        sb.AppendLine($"- Target framework: {extractedInfo.SelectedTargetFramework}");

        var readmePath = Path.Combine(moduleOutputDir, "README.md");
        await File.WriteAllTextAsync(readmePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        _logger.LogInformation("Wrote attribution README: {Path}", readmePath);
    }

    /// <summary>
    /// Copies the prebuilt generic loader DLL to the module's bin directory.
    /// First tries to extract from embedded resource, then falls back to disk locations.
    /// </summary>
    private async Task<string> CopyGenericLoaderAsync(
        string binDir,
        string loaderAssemblyName,
        CancellationToken cancellationToken)
    {
        const string loaderResourceName = "PowerShellRepackager.Resources.Svrooij.PowerShellRepackager.GenericLoader.dll";
        var destPath = Path.Combine(binDir, $"{loaderAssemblyName}.dll");

        // Extract from embedded resource
        try
        {
            var assembly = typeof(ModuleRepackager).Assembly;
            using var resourceStream = assembly.GetManifestResourceStream(loaderResourceName);

            if (resourceStream != null)
            {
                _logger.LogInformation("Extracting generic loader from embedded resource");
                await using (var destStream = File.Create(destPath))
                {
                    await resourceStream.CopyToAsync(destStream, cancellationToken);
                }

                // Give this copy a unique assembly identity so multiple repackaged modules can be
                // imported into the same PowerShell session (each gets its own loader + ALC + statics).
                AssemblyRenamer.RenameAssembly(destPath, loaderAssemblyName);
                _logger.LogInformation("Extracted generic loader to: {Dest} (assembly renamed to {Name})", destPath, loaderAssemblyName);
                return destPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract loader from embedded resource");
        }

        // If we get here, the embedded resource was not found
        throw new InvalidOperationException(
            $"Generic loader assembly not found in embedded resources ('{loaderResourceName}'). " +
            "Ensure the GenericLoader DLL is properly embedded as a resource in the PowerShellRepackager assembly.");
    }

    /// <summary>
    /// Returns a default manifest template if the original doesn't exist.
    /// Uses the RootModule + NestedModules pattern where the actual module DLL is the root,
    /// and the generic loader is a nested module that initializes the AssemblyLoadContext.
    /// </summary>
    private string GetDefaultManifestTemplate(string moduleName, Guid guid, string loaderFileName)
    {
        return $$"""
@{
    RootModule = 'bin/{{moduleName}}.dll'
    NestedModules = @('bin/{{loaderFileName}}')
    ModuleVersion = '1.0.0'
    GUID = '{{guid}}'
    Author = '{{RepackagedAuthor}}'
    CompanyName = '{{RepackagedCompany}}'
    Description = 'Repackaged module: {{moduleName}}'
    PowerShellVersion = '7.4'
    CompatiblePSEditions = @('Core')
    FunctionsToExport = @()
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()

    PrivateData = @{
        PSData = @{
            Tags = @('Repackaged', 'PSEdition_Core')
            ProjectUri = '{{RepackagerProjectUrl}}'
            ReleaseNotes = 'Repackaged using PowerShellRepackager'
        }
    }
}
""";
    }
}
