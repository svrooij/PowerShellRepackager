using Microsoft.ApplicationInsights.Channel;

namespace Microsoft.ApplicationInsights.DataContracts;

/// <summary>
/// Base class for telemetry items.
/// </summary>
public abstract class TelemetryBase : ITelemetry
{
    /// <summary>
    /// Initializes a new instance of the TelemetryBase class.
    /// </summary>
    protected TelemetryBase()
    {
        Timestamp = DateTimeOffset.UtcNow;
        Context = new TelemetryContext();
    }

    /// <summary>
    /// Gets or sets the timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the sequence number.
    /// </summary>
    public string? Sequence { get; set; }

    /// <summary>
    /// Gets the context.
    /// </summary>
    public TelemetryContext Context { get; }

    /// <summary>
    /// Sanitizes the telemetry item.
    /// </summary>
    public virtual void Sanitize()
    {
        // No-op: stub implementation
    }
}

/// <summary>
/// Represents an event telemetry item.
/// </summary>
public class EventTelemetry : TelemetryBase
{
    /// <summary>
    /// Initializes a new instance of the EventTelemetry class.
    /// </summary>
    public EventTelemetry()
    {
        Properties = new Dictionary<string, string>();
        Metrics = new Dictionary<string, double>();
    }

    /// <summary>
    /// Initializes a new instance with an event name.
    /// </summary>
    public EventTelemetry(string name) : this()
    {
        Name = name;
    }

    /// <summary>
    /// Gets or sets the event name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets the event properties.
    /// </summary>
    public IDictionary<string, string> Properties { get; }

    /// <summary>
    /// Gets the event metrics.
    /// </summary>
    public IDictionary<string, double> Metrics { get; }
}

/// <summary>
/// Represents an exception telemetry item.
/// </summary>
public class ExceptionTelemetry : TelemetryBase
{
    /// <summary>
    /// Initializes a new instance of the ExceptionTelemetry class.
    /// </summary>
    public ExceptionTelemetry()
    {
        Properties = new Dictionary<string, string>();
        Metrics = new Dictionary<string, double>();
    }

    /// <summary>
    /// Initializes a new instance with an exception.
    /// </summary>
    public ExceptionTelemetry(Exception exception) : this()
    {
        Exception = exception;
    }

    /// <summary>
    /// Gets or sets the exception.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets or sets the severity level.
    /// </summary>
    public SeverityLevel SeverityLevel { get; set; }

    /// <summary>
    /// Gets the exception properties.
    /// </summary>
    public IDictionary<string, string> Properties { get; }

    /// <summary>
    /// Gets the exception metrics.
    /// </summary>
    public IDictionary<string, double> Metrics { get; }
}

/// <summary>
/// Represents a trace telemetry item.
/// </summary>
public class TraceTelemetry : TelemetryBase
{
    /// <summary>
    /// Initializes a new instance of the TraceTelemetry class.
    /// </summary>
    public TraceTelemetry()
    {
        Properties = new Dictionary<string, string>();
    }

    /// <summary>
    /// Initializes a new instance with a message.
    /// </summary>
    public TraceTelemetry(string message) : this()
    {
        Message = message;
    }

    /// <summary>
    /// Initializes a new instance with a message and severity level.
    /// </summary>
    public TraceTelemetry(string message, SeverityLevel severityLevel) : this(message)
    {
        SeverityLevel = severityLevel;
    }

    /// <summary>
    /// Gets or sets the trace message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the severity level.
    /// </summary>
    public SeverityLevel SeverityLevel { get; set; }

    /// <summary>
    /// Gets the trace properties.
    /// </summary>
    public IDictionary<string, string> Properties { get; }
}

/// <summary>
/// Represents a metric telemetry item.
/// </summary>
public class MetricTelemetry : TelemetryBase
{
    /// <summary>
    /// Initializes a new instance of the MetricTelemetry class.
    /// </summary>
    public MetricTelemetry()
    {
        Properties = new Dictionary<string, string>();
    }

    /// <summary>
    /// Initializes a new instance with a metric name and value.
    /// </summary>
    public MetricTelemetry(string name, double value) : this()
    {
        Name = name;
        Sum = value;
    }

    /// <summary>
    /// Gets or sets the metric name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the metric sum.
    /// </summary>
    public double Sum { get; set; }

    /// <summary>
    /// Gets or sets the metric count.
    /// </summary>
    public int Count { get; set; } = 1;

    /// <summary>
    /// Gets or sets the minimum value.
    /// </summary>
    public double? Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum value.
    /// </summary>
    public double? Max { get; set; }

    /// <summary>
    /// Gets the metric properties.
    /// </summary>
    public IDictionary<string, string> Properties { get; }
}

/// <summary>
/// Represents a request telemetry item.
/// </summary>
public class RequestTelemetry : TelemetryBase
{
    /// <summary>
    /// Initializes a new instance of the RequestTelemetry class.
    /// </summary>
    public RequestTelemetry()
    {
        Properties = new Dictionary<string, string>();
        Metrics = new Dictionary<string, double>();
    }

    /// <summary>
    /// Gets or sets the request name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the response code.
    /// </summary>
    public string? ResponseCode { get; set; }

    /// <summary>
    /// Gets or sets the request success status.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the request duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets the request properties.
    /// </summary>
    public IDictionary<string, string> Properties { get; }

    /// <summary>
    /// Gets the request metrics.
    /// </summary>
    public IDictionary<string, double> Metrics { get; }
}

/// <summary>
/// Represents a dependency telemetry item.
/// </summary>
public class DependencyTelemetry : TelemetryBase
{
    /// <summary>
    /// Initializes a new instance of the DependencyTelemetry class.
    /// </summary>
    public DependencyTelemetry()
    {
        Properties = new Dictionary<string, string>();
        Metrics = new Dictionary<string, double>();
    }

    /// <summary>
    /// Gets or sets the dependency name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the dependency type.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the dependency target.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Gets or sets the success status.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets the dependency properties.
    /// </summary>
    public IDictionary<string, string> Properties { get; }

    /// <summary>
    /// Gets the dependency metrics.
    /// </summary>
    public IDictionary<string, double> Metrics { get; }
}

/// <summary>
/// Represents an availability result telemetry item.
/// </summary>
public class AvailabilityTelemetry : TelemetryBase
{
    /// <summary>
    /// Initializes a new instance of the AvailabilityTelemetry class.
    /// </summary>
    public AvailabilityTelemetry()
    {
        Properties = new Dictionary<string, string>();
        Metrics = new Dictionary<string, double>();
    }

    /// <summary>
    /// Gets or sets the availability test name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the success status.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the run location.
    /// </summary>
    public string? RunLocation { get; set; }

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets the properties.
    /// </summary>
    public IDictionary<string, string> Properties { get; }

    /// <summary>
    /// Gets the metrics.
    /// </summary>
    public IDictionary<string, double> Metrics { get; }
}
