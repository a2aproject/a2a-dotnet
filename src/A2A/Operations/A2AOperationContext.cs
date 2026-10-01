namespace A2A;

/// <summary>Provides host-neutral state to an A2A operation handler.</summary>
public sealed class A2AOperationContext
{
    /// <summary>Initializes a new operation context.</summary>
    /// <param name="requestHandler">The request-selected standard A2A handler.</param>
    /// <param name="features">Optional host and extension features.</param>
    public A2AOperationContext(
        IA2ARequestHandler requestHandler,
        A2AFeatureCollection? features = null)
    {
        ArgumentNullException.ThrowIfNull(requestHandler);
        RequestHandler = requestHandler;
        Features = features ?? new A2AFeatureCollection();
    }

    /// <summary>Gets the request-selected standard A2A handler.</summary>
    public IA2ARequestHandler RequestHandler { get; }

    /// <summary>Gets host and extension features for this request.</summary>
    public A2AFeatureCollection Features { get; }
}
