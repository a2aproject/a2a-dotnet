namespace A2A.V0_3Compat;

using Microsoft.AspNetCore.Http;
using System.Text.Json;

using V03 = A2A.V0_3;

/// <summary>Result type for returning v0.3-format JSON-RPC responses as JSON in HTTP responses.</summary>
internal sealed class V03JsonRpcResponseResult : IResult
{
    private readonly V03.JsonRpcResponse _response;

    internal V03JsonRpcResponseResult(V03.JsonRpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _response = response;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = StatusCodes.Status200OK;

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            _response,
            V03.A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(V03.JsonRpcResponse)),
            httpContext.RequestAborted).ConfigureAwait(false);
    }
}
