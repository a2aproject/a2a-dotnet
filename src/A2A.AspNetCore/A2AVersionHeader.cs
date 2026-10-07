using Microsoft.Extensions.Primitives;

namespace A2A.AspNetCore;

/// <summary>
/// Shared validation for the <c>A2A-Version</c> request header so the JSON-RPC and
/// HTTP+JSON bindings accept exactly the same protocol versions. See GitHub issue #512.
/// </summary>
internal static class A2AVersionHeader
{
    /// <summary>The request header that carries the negotiated A2A protocol version.</summary>
    internal const string HeaderName = "A2A-Version";

    private static readonly string[] s_supportedVersions = ["0.3", "1.0"];

    /// <summary>
    /// Validates the <c>A2A-Version</c> header. An absent or empty value is permitted (the
    /// binding applies its default version). Every present value must be a supported version:
    /// a single unsupported value, or any unsupported value among several (for example a
    /// repeated <c>A2A-Version</c> header carrying both a supported and an unsupported value),
    /// yields a <see cref="A2AErrorCode.VersionNotSupported"/> error. Checking all values
    /// rather than only the first prevents a second header value from bypassing the gate.
    /// </summary>
    /// <param name="headerValues">The raw header values (may be empty when the header is absent).</param>
    /// <returns>An <see cref="A2AException"/> describing the failure, or <see langword="null"/> when every value is acceptable.</returns>
    internal static A2AException? Validate(StringValues headerValues)
    {
        foreach (var version in headerValues)
        {
            if (!string.IsNullOrEmpty(version) && Array.IndexOf(s_supportedVersions, version) < 0)
            {
                return new A2AException(
                    $"Protocol version '{version}' is not supported. Supported versions: 0.3, 1.0",
                    A2AErrorCode.VersionNotSupported);
            }
        }

        return null;
    }
}
