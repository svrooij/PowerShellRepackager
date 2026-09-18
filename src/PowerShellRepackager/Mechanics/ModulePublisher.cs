using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PowerShellRepackager.Models;

namespace PowerShellRepackager.Mechanics;

/// <summary>
/// Packs a repackaged module folder into a NuGet package (.nupkg) and publishes it to a
/// PowerShell Gallery compatible (NuGet v2) feed using the NuGet push protocol.
/// </summary>
/// <remarks>
/// The PowerShell Gallery is a NuGet v2 feed. Publishing a module is a plain
/// <c>PUT {feed}/package/</c> with the package as multipart/form-data body and the API key in the
/// <c>X-NuGet-ApiKey</c> header — no PowerShellGet dependency required.
/// </remarks>
internal class ModulePublisher
{
    /// <summary>
    /// Default feed: the public PowerShell Gallery (NuGet v2 endpoint).
    /// </summary>
    public const string DefaultFeedUrl = "https://www.powershellgallery.com/api/v2";

    /// <summary>
    /// Environment variables probed (in order) for the API key when none is supplied.
    /// </summary>
    public static readonly string[] ApiKeyEnvironmentVariables = ["PSGALLERY_API_KEY", "NUGET_API_KEY"];

    private readonly HttpClient _httpClient;
    private readonly ILogger<ModulePublisher> _logger;

    public ModulePublisher(HttpClient httpClient, ILogger<ModulePublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the API key: explicit value first, then the known environment variables.
    /// </summary>
    public static string? ResolveApiKey(string? explicitApiKey)
    {
        if (!string.IsNullOrWhiteSpace(explicitApiKey))
            return explicitApiKey;

        foreach (var variable in ApiKeyEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Creates a .nupkg for the repackaged module next to its output directory.
    /// </summary>
    /// <returns>Full path of the created package.</returns>
    public async Task<string> CreatePackageAsync(ModuleRepackageInfo repackageInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repackageInfo);

        var manifest = await File.ReadAllTextAsync(repackageInfo.ManifestPath, cancellationToken);
        var metadata = ReadMetadata(manifest, repackageInfo);

        var packageDir = Path.GetDirectoryName(repackageInfo.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? repackageInfo.OutputDirectory;
        var packagePath = Path.Combine(packageDir, $"{metadata.Id}.{metadata.Version}.nupkg");

        if (File.Exists(packagePath))
            File.Delete(packagePath);

        _logger.LogInformation("Creating package {Package}", packagePath);

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, $"{metadata.Id}.nuspec", BuildNuspec(metadata), cancellationToken);
            await WriteEntryAsync(archive, "_rels/.rels", BuildRels(metadata.Id), cancellationToken);
            await WriteEntryAsync(archive, "[Content_Types].xml", BuildContentTypes(), cancellationToken);

            foreach (var file in Directory.EnumerateFiles(repackageInfo.OutputDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(repackageInfo.OutputDirectory, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
            }
        }

        _logger.LogInformation("Created package {Package} ({Size:N0} bytes)", packagePath, new FileInfo(packagePath).Length);
        return packagePath;
    }

    /// <summary>
    /// Pushes a .nupkg to a NuGet v2 feed.
    /// </summary>
    public async Task PublishAsync(string packagePath, string feedUrl, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        if (!File.Exists(packagePath))
            throw new FileNotFoundException($"Package not found: {packagePath}", packagePath);

        var pushUrl = BuildPushUrl(feedUrl);
        _logger.LogInformation("Publishing {Package} to {Feed}", Path.GetFileName(packagePath), pushUrl);

        await using var fileStream = File.OpenRead(packagePath);
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "package", Path.GetFileName(packagePath));

        using var request = new HttpRequestMessage(HttpMethod.Put, pushUrl) { Content = content };
        request.Headers.Add("X-NuGet-ApiKey", apiKey);
        request.Headers.Add("X-NuGet-Client-Version", "6.0.0");
        request.Headers.UserAgent.ParseAdd("PowerShellRepackager");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Published {Package} ({StatusCode})", Path.GetFileName(packagePath), (int)response.StatusCode);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = $"Publishing to '{pushUrl}' failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
        if (!string.IsNullOrWhiteSpace(body))
            message += $" {body.Trim()}";

        throw response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                => new UnauthorizedAccessException(message),
            System.Net.HttpStatusCode.Conflict
                => new InvalidOperationException($"{message} A package with this version already exists on the feed."),
            _ => new HttpRequestException(message, null, response.StatusCode),
        };
    }

    /// <summary>
    /// Normalizes a feed URL (e.g. <c>https://www.powershellgallery.com/api/v2</c>) to the NuGet v2 push endpoint.
    /// </summary>
    internal static Uri BuildPushUrl(string feedUrl)
    {
        var trimmed = feedUrl.TrimEnd('/');
        if (trimmed.EndsWith("/package", StringComparison.OrdinalIgnoreCase))
            return new Uri(trimmed + "/", UriKind.Absolute);

        return new Uri(trimmed + "/package/", UriKind.Absolute);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string entryName, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static string BuildNuspec(PackageMetadata metadata)
    {
        XNamespace ns = "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd";
        var meta = new XElement(ns + "metadata",
            new XElement(ns + "id", metadata.Id),
            new XElement(ns + "version", metadata.Version),
            new XElement(ns + "authors", metadata.Authors),
            new XElement(ns + "owners", metadata.Authors),
            new XElement(ns + "requireLicenseAcceptance", "false"),
            new XElement(ns + "description", metadata.Description));

        if (metadata.Copyright is not null)
            meta.Add(new XElement(ns + "copyright", metadata.Copyright));
        if (metadata.ProjectUri is not null)
            meta.Add(new XElement(ns + "projectUrl", metadata.ProjectUri));
        if (metadata.LicenseUri is not null)
            meta.Add(new XElement(ns + "licenseUrl", metadata.LicenseUri));
        if (metadata.ReleaseNotes is not null)
            meta.Add(new XElement(ns + "releaseNotes", metadata.ReleaseNotes));

        meta.Add(new XElement(ns + "tags", string.Join(' ', metadata.Tags)));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(ns + "package", meta));
        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    private static string BuildRels(string id) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{id}.nuspec" Id="R1" />
        </Relationships>
        """;

    private static string BuildContentTypes() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="nuspec" ContentType="application/octet" />
          <Default Extension="psd1" ContentType="application/octet" />
          <Default Extension="psm1" ContentType="application/octet" />
          <Default Extension="ps1" ContentType="application/octet" />
          <Default Extension="ps1xml" ContentType="application/octet" />
          <Default Extension="dll" ContentType="application/octet" />
          <Default Extension="json" ContentType="application/octet" />
          <Default Extension="xml" ContentType="application/octet" />
          <Default Extension="txt" ContentType="application/octet" />
          <Default Extension="md" ContentType="application/octet" />
        </Types>
        """;

    private static PackageMetadata ReadMetadata(string manifest, ModuleRepackageInfo info)
    {
        var version = ReadScalar(manifest, "ModuleVersion") ?? info.OriginalModuleVersion;
        var author = ReadScalar(manifest, "Author") ?? "Unknown";
        var description = ReadScalar(manifest, "Description")
            ?? $"Repackaged version of {info.OriginalModuleName} {info.OriginalModuleVersion} with isolated assembly loading.";

        var tags = new List<string> { "PSModule", "PSEdition_Core", "Repackaged" };
        tags.AddRange(ReadArray(manifest, "Tags")
            .Where(t => Regex.IsMatch(t, "^[A-Za-z0-9_.-]+$"))
            .Where(t => !tags.Contains(t, StringComparer.OrdinalIgnoreCase)));

        if (ReadArray(manifest, "CmdletsToExport").Any(c => c != "*"))
            tags.Add("PSIncludes_Cmdlet");
        if (ReadArray(manifest, "FunctionsToExport").Any(f => f != "*"))
            tags.Add("PSIncludes_Function");

        return new PackageMetadata(
            info.RepackagedModuleName,
            version,
            author,
            description,
            ReadScalar(manifest, "Copyright"),
            ReadScalar(manifest, "ProjectUri"),
            ReadScalar(manifest, "LicenseUri"),
            ReadScalar(manifest, "ReleaseNotes"),
            tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? ReadScalar(string manifest, string field)
    {
        var match = Regex.Match(manifest, $@"(?m)^(?!\s*#)\s*{Regex.Escape(field)}\s*=\s*(['""])(.*?)\1\s*$");
        if (!match.Success)
            return null;

        var value = match.Groups[2].Value.Trim();
        return value.Length == 0 ? null : value;
    }

    private static string[] ReadArray(string manifest, string field)
    {
        var match = Regex.Match(manifest, $@"(?ms)^(?!\s*#)\s*{Regex.Escape(field)}\s*=\s*(@\((?<body>.*?)\)|(?<body>[^\r\n]+))\s*$");
        if (!match.Success)
            return [];

        var body = Regex.Replace(match.Groups["body"].Value, @"(?m)^\s*#.*$", string.Empty);
        return Regex.Matches(body, @"(['""])(.*?)\1")
            .Select(m => m.Groups[2].Value.Trim())
            .Where(v => v.Length > 0)
            .ToArray();
    }

    private sealed record PackageMetadata(
        string Id,
        string Version,
        string Authors,
        string Description,
        string? Copyright,
        string? ProjectUri,
        string? LicenseUri,
        string? ReleaseNotes,
        string[] Tags);
}
