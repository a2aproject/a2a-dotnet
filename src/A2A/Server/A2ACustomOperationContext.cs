using System.Diagnostics.CodeAnalysis;

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
    /// Gets the immutable extension activation snapshot supplied for this request, if any.
    /// </summary>
    /// <remarks>
    /// Returns the existing <see cref="A2ARequestContext"/> feature, or <see langword="null"/>
    /// when no snapshot was supplied. A present snapshot may contain no activated extensions.
    /// This property does not negotiate extensions or reevaluate their availability.
    /// </remarks>
    public A2ARequestContext? ExtensionActivation =>
        TryGetFeature<A2ARequestContext>(out var activation) ? activation : null;

    /// <summary>
    /// Gets a required request feature by assignable type.
    /// </summary>
    public TFeature GetRequiredFeature<TFeature>() where TFeature : class
    {
        if (TryGetFeature<TFeature>(out var feature))
        {
            return feature;
        }

        throw new InvalidOperationException(
            $"Custom operation feature '{typeof(TFeature).FullName}' is not available.");
    }

    /// <summary>
    /// Tries to get a request feature by assignable type.
    /// </summary>
    /// <typeparam name="TFeature">The requested feature type.</typeparam>
    /// <param name="feature">The feature when found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when an assignable feature is available; otherwise, <see langword="false"/>.</returns>
    public bool TryGetFeature<TFeature>([NotNullWhen(true)] out TFeature? feature) where TFeature : class
    {
        foreach (var value in _features.Values)
        {
            if (value is TFeature typedFeature)
            {
                feature = typedFeature;
                return true;
            }
        }

        feature = null;
        return false;
    }
}
