using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Svrooij.PowerShell.DI;
using Svrooij.PowerShell.DI.Logging;

namespace PowerShellRepackager;
/// <inheritdoc/>
public class Startup : PsStartup
{
    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Add HttpClient with a longer timeout for downloading large modules
        services.AddSingleton<HttpClient>((_) =>
        {
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(180); // For downloading large modules
            return httpClient;
        });

        // Mechanics for doing the actual work of downloading and repackaging modules
        services.AddTransient<Mechanics.PackageDownloader>();
        services.AddTransient<Mechanics.PackageExtractor>();
        services.AddTransient<Mechanics.ModuleRepackager>();
        services.AddTransient<Mechanics.ModulePublisher>();
    }

    /// <inheritdoc/>
    public override Action<PowerShellLoggerConfiguration> ConfigurePowerShellLogging()
    {
        return builder =>
        {
            builder.DefaultLevel = LogLevel.Debug;
            builder.LogLevel.Add("System.Net.Http.HttpClient", LogLevel.Warning);
            builder.LogLevel.Add("System.Net.Http.HttpClient.GraphClientFactory.LogicalHandler", LogLevel.Warning);
            builder.IncludeCategory = true;
            builder.StripNamespace = true;
        };
    }
}