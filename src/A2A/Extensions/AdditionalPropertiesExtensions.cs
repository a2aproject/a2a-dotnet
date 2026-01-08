using Microsoft.Extensions.AI;
using System.Text.Json;

namespace A2A;

/// <summary>
/// Provides extension methods for converting between <see cref="AdditionalPropertiesDictionary"/> and A2A metadata dictionaries.
/// </summary>
public static class AdditionalPropertiesExtensions
{
    /// <summary>
    /// Creates an <see cref="AdditionalPropertiesDictionary"/> from A2A metadata.
    /// </summary>
    /// <param name="metadata">The A2A metadata dictionary to convert.</param>
    /// <returns>An <see cref="AdditionalPropertiesDictionary"/> containing the metadata, or <see langword="null"/> if the metadata is empty.</returns>
    public static AdditionalPropertiesDictionary? ToAdditionalProperties(this Dictionary<string, JsonElement>? metadata)
    {
        if (metadata is not { Count: > 0 })
        {
            return null;
        }

        AdditionalPropertiesDictionary props = [];
        foreach (var kvp in metadata)
        {
            props[kvp.Key] = kvp.Value;
        }

        return props;
    }

    /// <summary>
    /// Creates an A2A metadata dictionary from an <see cref="AdditionalPropertiesDictionary"/>.
    /// </summary>
    /// <param name="additionalProperties">The additional properties dictionary to convert.</param>
    /// <returns>A dictionary of A2A metadata, or <see langword="null"/> if the additional properties dictionary is empty.</returns>
    public static Dictionary<string, JsonElement>? ToA2AMetadata(this AdditionalPropertiesDictionary? additionalProperties)
    {
        if (additionalProperties is not { Count: > 0 })
        {
            return null;
        }

        var metadata = new Dictionary<string, JsonElement>();

        foreach (var kvp in additionalProperties)
        {
            metadata[kvp.Key] = JsonSerializer.SerializeToElement(kvp.Value, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
        }

        return metadata;
    }
}
