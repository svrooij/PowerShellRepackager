using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerShellRepackager.Mechanics;

internal class PackageDownloader
{
    private readonly ILogger<PackageDownloader> _logger;
    private readonly HttpClient _httpClient;

    private static readonly string CacheFolder = Path.Combine(Path.GetTempPath(), "PowerShellRepackager");

    public PackageDownloader(ILogger<PackageDownloader> logger, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<string> DownloadPackageAsync(string moduleName, string version, bool force, CancellationToken cancellationToken)
    {
        var fileName = $"{moduleName}.{version}.nupkg";
        var filePath = Path.Combine(CacheFolder, fileName);
        if (!Directory.Exists(CacheFolder))
        {
            Directory.CreateDirectory(CacheFolder);
        }

        if (File.Exists(filePath))
        {
            if (!force)
            {
                _logger.LogInformation("Module package {FilePath} already exists. Skipping download.", filePath);
                return filePath;
            }
        }

        var url = $"https://www.powershellgallery.com/api/v2/package/{moduleName}/{version}";
        _logger.LogInformation("Downloading module package from {Url} to {FilePath}", url, filePath);
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);
        _logger.LogInformation("Module package downloaded to {FilePath}", filePath);
        return filePath;
    }
}
