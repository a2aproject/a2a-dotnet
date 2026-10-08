namespace A2A;

/// <summary>
/// Carries request-scoped host services and transport features to a custom operation handler.
/// </summary>
public sealed class A2ACustomOperationContext
{
    private readonly Dictionary<Type, object> _features;

    /// <summary>
    /// Initializes a custom operation context.
    /// </summary>
    /// <param name="requestServices">Request-scoped services, when available.</param>
    /// <param name="features">Transport or host-specific request features.</param>
    public A2ACustomOperationContext(
        IServiceProvider? requestServices = null,
        params object[] features)
    {
        RequestServices = requestServices;
        _features = features.ToDictionary(feature => feature.GetType());
    }

    /// <summary>
    /// Gets request-scoped services, when supplied by the transport.
    /// </summary>
    public IServiceProvider? RequestServices { get; }

    /// <summary>
    /// Gets a required request feature by assignable type.
    /// </summary>
    public TFeature GetRequiredFeature<TFeature>() where TFeature : class
    {
        foreach (var feature in _features.Values)
        {
            if (feature is TFeature typedFeature)
            {
                return typedFeature;
            }
        }

        throw new InvalidOperationException(
            $"Custom operation feature '{typeof(TFeature).FullName}' is not available.");
    }
}
