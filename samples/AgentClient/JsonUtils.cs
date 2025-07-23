using System.Text.Json;

namespace AgentClient;

internal static class JsonUtils
{
    public static JsonElement CreateJsonElement(object? value)
    {
        var json = JsonSerializer.Serialize(value);

        using JsonDocument document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
