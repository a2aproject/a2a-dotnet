namespace A2A;

/// <summary>Stores typed host and extension state for one operation invocation.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Collection accurately describes the type-keyed feature container.")]
public sealed class A2AFeatureCollection
{
    private readonly Dictionary<Type, object> _features = [];

    /// <summary>Adds or replaces a feature by its declared type.</summary>
    /// <typeparam name="TFeature">The feature type.</typeparam>
    /// <param name="feature">The feature instance.</param>
    public void Set<TFeature>(TFeature feature)
        where TFeature : notnull
    {
        ArgumentNullException.ThrowIfNull(feature);
        _features[typeof(TFeature)] = feature;
    }

    /// <summary>Gets a required feature.</summary>
    /// <typeparam name="TFeature">The feature type.</typeparam>
    /// <returns>The registered feature.</returns>
    /// <exception cref="InvalidOperationException">The feature is not registered.</exception>
    public TFeature GetRequired<TFeature>()
        where TFeature : notnull
    {
        if (_features.TryGetValue(typeof(TFeature), out var feature))
        {
            return (TFeature)feature;
        }

        throw new InvalidOperationException(
            $"No A2A operation feature is registered for '{typeof(TFeature).FullName}'.");
    }
}
