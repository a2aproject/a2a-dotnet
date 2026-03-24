namespace A2A;

using System.Text.Json.Serialization;

/// <summary>
/// Represents an HTTP+JSON error response following AIP-193 / google.rpc.Status format
/// per A2A spec Section 11.6.
/// </summary>
internal sealed class A2AErrorResponse
{
    /// <summary>The error object.</summary>
    [JsonPropertyName("error")]
    public A2AErrorStatus? Error { get; set; }
}

/// <summary>
/// Maps to the google.rpc.Status fields: code, status, message, details.
/// </summary>
internal sealed class A2AErrorStatus
{
    /// <summary>The HTTP status code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>The gRPC status string (e.g. "NOT_FOUND").</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>A human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Array of detail objects, typically containing google.rpc.ErrorInfo.</summary>
    [JsonPropertyName("details")]
    public List<A2AErrorDetail>? Details { get; set; }
}

/// <summary>
/// Represents a google.rpc.ErrorInfo entry in the details array.
/// </summary>
internal sealed class A2AErrorDetail
{
    /// <summary>The protobuf type URL (e.g. "type.googleapis.com/google.rpc.ErrorInfo").</summary>
    [JsonPropertyName("@type")]
    public string? Type { get; set; }

    /// <summary>The A2A error reason in UPPER_SNAKE_CASE (e.g. "TASK_NOT_FOUND").</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>The error domain (e.g. "a2a-protocol.org").</summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }
}
