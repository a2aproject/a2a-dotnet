using System.Text.Json;

namespace A2A;

public class JsonRpcError
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    public JsonElement? Data { get; set; }

    // Deserialize a JsonRpcError from a JsonElement
    public static JsonRpcError FromJson(JsonElement jsonElement) =>
        jsonElement.Deserialize(A2AJsonUtilities.JsonContext.Default.JsonRpcError) ??
        throw new InvalidOperationException("Failed to deserialize JsonRpcError.");

    // Serialize a JsonRpcError to JSON
    public string ToJson() => JsonSerializer.Serialize(this, A2AJsonUtilities.JsonContext.Default.JsonRpcError);
}