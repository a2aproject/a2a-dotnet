namespace A2A.Core;

/// <summary>
/// Error for method not found
/// </summary>
public class MethodNotFoundError : JsonRpcError
{
    /// <summary>
    /// Creates a new method not found error
    /// </summary>
    public MethodNotFoundError()
    {
        Code = -32601;
        Message = "Method not found";
    }
}


