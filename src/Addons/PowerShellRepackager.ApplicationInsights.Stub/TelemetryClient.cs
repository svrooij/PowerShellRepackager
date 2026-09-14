using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;

namespace Microsoft.ApplicationInsights;

/// <summary>
/// No-op stub for TelemetryClient.
/// Provides the main public API for sending telemetry, all operations are no-ops.
/// </summary>
public class TelemetryClient : IDisposable
{
    private TelemetryConfiguration _telemetryConfiguration;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the TelemetryClient class.
    /// </summary>
    public TelemetryClient()
        : this(TelemetryConfiguration.Active)
    {
    }

    /// <summary>
    /// Initializes a new instance of the TelemetryClient class with a specific configuration.
    /// </summary>
    public TelemetryClient(TelemetryConfiguration telemetryConfiguration)
    {
        _telemetryConfiguration = telemetryConfiguration ?? throw new ArgumentNullException(nameof(telemetryConfiguration));
    }

    /// <summary>
    /// Gets or sets the telemetry configuration.
    /// </summary>
    public TelemetryConfiguration TelemetryConfiguration
    {
        get => _telemetryConfiguration;
        set => _telemetryConfiguration = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the context associated with the client.
    /// </summary>
    public TelemetryContext Context => _telemetryConfiguration.TelemetryChannel is InMemoryChannel channel
        ? channel.Context
        : new TelemetryContext();

    /// <summary>
    /// Tracks an event (no-op).
    /// </summary>
    public void TrackEvent(string eventName)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks an event with properties (no-op).
    /// </summary>
    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null, IDictionary<string, double>? metrics = null)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks an exception (no-op).
    /// </summary>
    public void TrackException(Exception exception, IDictionary<string, string>? properties = null, IDictionary<string, double>? metrics = null)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks a trace message (no-op).
    /// </summary>
    public void TrackTrace(string message)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks a trace message with severity and properties (no-op).
    /// </summary>
    public void TrackTrace(string message, SeverityLevel severityLevel, IDictionary<string, string>? properties = null)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks a metric (no-op).
    /// </summary>
    public void TrackMetric(string name, double value, IDictionary<string, string>? properties = null)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks a request (no-op).
    /// </summary>
    public void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks a dependency (no-op).
    /// </summary>
    public void TrackDependency(string typeName, string name, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks availability result (no-op).
    /// </summary>
    public void TrackAvailability(string name, DateTimeOffset timeStamp, TimeSpan duration, string runLocation, bool success, string? message = null)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Tracks a generic telemetry item (no-op).
    /// </summary>
    public void Track(ITelemetry telemetry)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Flushes any pending telemetry (no-op).
    /// </summary>
    public void Flush()
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Disposes the telemetry client.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _telemetryConfiguration?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Severity levels for trace messages.
/// </summary>
public enum SeverityLevel
{
    Verbose = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}
