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