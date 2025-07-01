namespace A2A.Core;

/// <summary>
/// Error for JSON parsing failures
/// </summary>
public class JsonParseError : JsonRpcError
{
    /// <summary>
    /// Creates a new JSON parse error
    /// </summary>
    public JsonParseError()
    {
        Code = -32700;
        Message = "Invalid JSON payload";
    }
}


