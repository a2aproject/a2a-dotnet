using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace A2A.AspNetCore;

/// <summary>
/// Result type for returning JSON-RPC responses as JSON in HTTP responses.
/// </summary>
/// <remarks>
/// Implements IResult to provide custom serialization of JSON-RPC response objects
/// with appropriate HTTP status codes based on success or error conditions.
/// </remarks>
public class JsonRpcResponseResult : IResult
{
    private readonly JsonRpcResponse jsonRpcResponse;

    /// <summary>
    /// Initializes a new instance of the JsonRpcResponseResult class.
    /// </summary>
    /// <param name="jsonRpcResponse">The JSON-RPC response object to serialize and return in the HTTP response.</param>
    public JsonRpcResponseResult(JsonRpcResponse jsonRpcResponse)
    {
        ArgumentNullException.ThrowIfNull(jsonRpcResponse);

        this.jsonRpcResponse = jsonRpcResponse;
    }

    /// <summary>
    /// Executes the result by serializing the JSON-RPC response as JSON to the HTTP response body.
    /// </summary>
    /// <remarks>
    /// Sets the appropriate content type and HTTP status code (200 for success, 400 for JSON-RPC errors).
    /// Uses the default A2A JSON serialization options for consistent formatting.
    /// </remarks>
    /// <param name="httpContext">The HTTP context to write the response to.</param>
    /// <returns>A task representing the asynchronous serialization operation.</returns>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = StatusCodes.Status200OK;

        await JsonSerializer.SerializeAsync(httpContext.Response.Body, jsonRpcResponse, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcResponse)));
    }
}