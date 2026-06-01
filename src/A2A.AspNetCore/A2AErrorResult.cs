using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace A2A.AspNetCore;

/// <summary>
/// An <see cref="IResult"/> that writes a google.rpc.Status JSON error response
/// per A2A spec Section 11.6.
/// </summary>
/// <param name="exception">The A2A exception to render as an error response.</param>
internal sealed class A2AErrorResult(A2AException exception) : IResult, IStatusCodeHttpResult
{
    public int? StatusCode => A2AErrorCodeMapping.GetHttpStatusCode(exception.ErrorCode);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var errorCode = exception.ErrorCode;
        var httpStatus = A2AErrorCodeMapping.GetHttpStatusCode(errorCode);

        httpContext.Response.StatusCode = httpStatus;
        httpContext.Response.ContentType = "application/json";

        // Build JSON into a buffer first (Utf8JsonWriter on MemoryStream is synchronous-safe),
        // then copy to the response body asynchronously to avoid AllowSynchronousIO violations.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", httpStatus);
            writer.WriteString("status", A2AErrorCodeMapping.GetGrpcStatus(errorCode));
            writer.WriteString("message", exception.Message);
            writer.WritePropertyName("details");
            writer.WriteStartArray();

            if (A2AErrorCodeMapping.IsA2ASpecificError(errorCode))
            {
                var reason = A2AErrorCodeMapping.GetReasonString(errorCode);
                if (reason is not null)
                {
                    writer.WriteStartObject();
                    writer.WriteString("@type", "type.googleapis.com/google.rpc.ErrorInfo");
                    writer.WriteString("reason", reason);
                    writer.WriteString("domain", "a2a-protocol.org");
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(httpContext.Response.Body);
    }
}
