namespace A2A.AspNetCore;

/// <summary>
/// Request/response transport limits enforced by the A2A endpoints.
/// </summary>
internal static class A2ARequestLimits
{
    /// <summary>Maximum accepted request body size in bytes (10 MB).</summary>
    public const long MaxRequestBodySize = 10 * 1024 * 1024;

    /// <summary>Maximum accepted request body size in megabytes, for user-facing messages.</summary>
    public const long MaxRequestBodySizeInMb = MaxRequestBodySize / (1024 * 1024);
}
