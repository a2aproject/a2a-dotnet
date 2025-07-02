namespace A2A;

/// <summary>
/// Error for invalid requests
/// </summary>
public class InvalidRequestError : JsonRpcError
{
    /// <summary>
    /// Creates a new invalid request error
    /// </summary>
    public InvalidRequestError()
    {
        Code = -32600;
        Message = "Request payload validation error";
    }
}


