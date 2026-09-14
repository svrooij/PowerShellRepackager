namespace Microsoft.ApplicationInsights.Channel;

/// <summary>
/// Represents a base interface for telemetry items.
/// </summary>
public interface ITelemetry
{
    /// <summary>
    /// Gets or sets the timestamp of the telemetry item.
    /// </summary>
    DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the sequence number of the telemetry item.
    /// </summary>
    string? Sequence { get; set; }

    /// <summary>
    /// Gets or sets the context associated with the telemetry item.
    /// </summary>
    TelemetryContext Context { get; }

    /// <summary>
    /// Sanitizes the telemetry item (no-op in stub).
    /// </summary>
    void Sanitize();
}

/// <summary>
/// Represents a telemetry context.
/// </summary>
public class TelemetryContext
{
    /// <summary>
    /// Gets or sets the user context.
    /// </summary>
    public Dictionary<string, string> Properties { get; } = new();

    /// <summary>
    /// Gets or sets the instrumentation key.
    /// </summary>
    public string? InstrumentationKey { get; set; }
}

/// <summary>
/// Represents an interface for telemetry initializers.
/// </summary>
public interface ITelemetryInitializer
{
    /// <summary>
    /// Initializes the telemetry item (no-op in stub).
    /// </summary>
    void Initialize(ITelemetry telemetry);
}

/// <summary>
/// Represents an interface for telemetry processors.
/// </summary>
public interface ITelemetryProcessor
{
    /// <summary>
    /// Processes the telemetry item (no-op in stub).
    /// </summary>
    void Process(ITelemetry item);
}

/// <summary>
/// Represents an interface for telemetry channels.
/// </summary>
public interface ITelemetryChannel : IDisposable
{
    /// <summary>
    /// Gets or sets the instrumentation key for the channel.
    /// </summary>
    string? EndpointAddress { get; set; }

    /// <summary>
    /// Gets or sets whether the channel is in developer mode.
    /// </summary>
    bool? DeveloperMode { get; set; }

    /// <summary>
    /// Sends the telemetry item (no-op in stub).
    /// </summary>
    void Send(ITelemetry item);

    /// <summary>
    /// Flushes any pending telemetry items (no-op in stub).
    /// </summary>
    void Flush();
}
