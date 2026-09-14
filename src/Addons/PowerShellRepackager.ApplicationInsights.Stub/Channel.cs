namespace Microsoft.ApplicationInsights.Channel;

/// <summary>
/// In-memory telemetry channel that buffers telemetry items without sending them.
/// Used as the default channel for the no-op stub.
/// </summary>
public class InMemoryChannel : ITelemetryChannel
{
    private readonly List<ITelemetry> _buffer = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the InMemoryChannel class.
    /// </summary>
    public InMemoryChannel()
    {
        Context = new TelemetryContext();
        EndpointAddress = "https://dc.applicationinsights.azure.com/v2/track";
        DeveloperMode = false;
    }

    /// <summary>
    /// Gets the context for the channel.
    /// </summary>
    public TelemetryContext Context { get; }

    /// <summary>
    /// Gets or sets the endpoint address.
    /// </summary>
    public string? EndpointAddress { get; set; }

    /// <summary>
    /// Gets or sets whether the channel is in developer mode.
    /// </summary>
    public bool? DeveloperMode { get; set; }

    /// <summary>
    /// Gets the buffered telemetry items (for diagnostics).
    /// </summary>
    public IReadOnlyList<ITelemetry> Buffer => _buffer.AsReadOnly();

    /// <summary>
    /// Sends a telemetry item (buffers it in memory, no actual transmission).
    /// </summary>
    public void Send(ITelemetry item)
    {
        if (item != null)
        {
            lock (_buffer)
            {
                _buffer.Add(item);
            }
        }
    }

    /// <summary>
    /// Flushes pending items (no-op, just clears the buffer).
    /// </summary>
    public void Flush()
    {
        lock (_buffer)
        {
            _buffer.Clear();
        }
    }

    /// <summary>
    /// Disposes the channel.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Flush();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Synchronous telemetry channel that immediately sends telemetry (no-op in stub).
/// </summary>
public class SynchronousChannel : ITelemetryChannel
{
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the SynchronousChannel class.
    /// </summary>
    public SynchronousChannel()
    {
        EndpointAddress = "https://dc.applicationinsights.azure.com/v2/track";
    }

    /// <summary>
    /// Gets or sets the endpoint address.
    /// </summary>
    public string? EndpointAddress { get; set; }

    /// <summary>
    /// Gets or sets whether the channel is in developer mode.
    /// </summary>
    public bool? DeveloperMode { get; set; }

    /// <summary>
    /// Sends a telemetry item (no-op).
    /// </summary>
    public void Send(ITelemetry item)
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Flushes pending items (no-op).
    /// </summary>
    public void Flush()
    {
        // No-op: stub implementation
    }

    /// <summary>
    /// Disposes the channel.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
