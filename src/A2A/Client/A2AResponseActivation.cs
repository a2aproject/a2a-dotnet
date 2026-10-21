using System.Collections.Frozen;

namespace A2A;

/// <summary>Reads immutable response metadata without masking the original protocol failure.</summary>
internal static class A2AResponseActivation
{
    internal static IReadOnlySet<string> Read(IEnumerable<string?> values)
    {
        try
        {
            return A2AExtensionIdentifiers.Parse(values).ToFrozenSet(StringComparer.Ordinal);
        }
        catch (A2AException)
        {
            // A malformed optional response parameter must not replace the real operation error.
            return FrozenSet<string>.Empty;
        }
    }

    internal static IReadOnlySet<string> Read(HttpResponseMessage response)
        => Read(response.Headers.TryGetValues("A2A-Extensions", out var values) ? values : []);
}
