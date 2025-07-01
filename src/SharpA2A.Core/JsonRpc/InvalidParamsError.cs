namespace A2A.Core;

/// <summary>
/// Error for invalid parameters
/// </summary>
public class InvalidParamsError : JsonRpcError
{
    /// <summary>
    /// Creates a new invalid parameters error
    /// </summary>
    public InvalidParamsError()
    {
        Code = -32602;
        Message = "Invalid parameters";
    }
}


