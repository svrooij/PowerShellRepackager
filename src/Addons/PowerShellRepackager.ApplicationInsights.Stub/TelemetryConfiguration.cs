using Microsoft.ApplicationInsights.Channel;

namespace Microsoft.ApplicationInsights;

/// <summary>
/// No-op stub for TelemetryConfiguration.
/// Provides a minimal configuration object for Application Insights telemetry.
/// </summary>
public class TelemetryConfiguration : IDisposable
{
    private static TelemetryConfiguration? _activeConfiguration;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the TelemetryConfiguration class.
    /// </summary>
    public TelemetryConfiguration()
    {
        InstrumentationKey = string.Empty;
        TelemetryChannel = new InMemoryChannel();
        TelemetryInitializers = new List<ITelemetryInitializer>();
        TelemetryProcessors = new List<ITelemetryProcessor>();
    }

    /// <summary>
    /// Initializes a new instance of the TelemetryConfiguration class with an instrumentation key.
    /// </summary>
    public TelemetryConfiguration(string instrumentationKey)
        : this()
    {
        InstrumentationKey = instrumentationKey;
    }

    /// <summary>
    /// Gets or sets the instrumentation key.
    /// </summary>
    public string InstrumentationKey { get; set; }

    /// <summary>
    /// Gets or sets the telemetry channel.
    /// </summary>
    public ITelemetryChannel? TelemetryChannel { get; set; }

    /// <summary>
    /// Gets the collection of telemetry initializers.
    /// </summary>
    public IList<ITelemetryInitializer> TelemetryInitializers { get; }

    /// <summary>
    /// Gets the collection of telemetry processors.
    /// </summary>
    public IList<ITelemetryProcessor> TelemetryProcessors { get; }

    /// <summary>
    /// Gets or sets the endpoint address for telemetry transmission.
    /// </summary>
    public string? EndpointAddress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether developer mode is enabled.
    /// </summary>
    public bool DeveloperMode { get; set; }

    /// <summary>
    /// Gets the active TelemetryConfiguration instance.
    /// </summary>
    public static TelemetryConfiguration Active
    {
        get
        {
            _activeConfiguration ??= new TelemetryConfiguration();
            return _activeConfiguration;
        }
    }

    /// <summary>
    /// Creates a new TelemetryConfiguration from an instrumentation key.
    /// </summary>
    public static TelemetryConfiguration CreateDefault()
    {
        return new TelemetryConfiguration();
    }

    /// <summary>
    /// Disposes the telemetry configuration and its channel.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        TelemetryChannel?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
