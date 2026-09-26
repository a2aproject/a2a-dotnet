// ACTS authentication support.
//
// Several ACTS tests assert that an agent requiring a credential rejects a
// request that lacks one. A2A conditions that obligation on the agent's own
// declared requirements, so those tests gate on a card declaring
// securitySchemes and securityRequirements — and an agent declaring neither is
// not violating anything by serving an unauthenticated request. Every ITK agent
// declares neither by default, because traversal peers dial it with no
// credential.
//
// So enforcement is opt-in, and the ACTS runner turns it on for a separate pass
// over just those tests. It cannot be on for the main pass: the runner sends
// raw steps exactly as written, so an absent Authorization header means "reject
// me" in SEC-AUTH-001 and "serve me" in JSONRPC-ENV-001, and no server can tell
// those two requests apart.
//
// The extended card is the exception, guarded whatever the mode — A2A §13.3
// makes its authentication unconditional, and no traversal scenario fetches
// one.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace A2A.Itk;

/// <summary>Bearer-credential enforcement for the ACTS conformance suite.</summary>
public static class ActsAuth
{
    /// <summary>The credential the ACTS runner presents on every abstract operation.</summary>
    /// <remarks>
    /// Not a secret: the runner attaches this to every abstract operation and offers
    /// <see cref="InsufficientToken"/> from SEC-AUTH-002 and SEC-EXTCARD-002, so a fixture
    /// has to recognise both to answer 200 / 403 / 401 as those tests require.
    /// </remarks>
    public const string ValidToken = "itk-valid-token";

    /// <summary>A credential that authenticates but does not authorize.</summary>
    public const string InsufficientToken = "itk-insufficient-token";

    /// <summary>The scheme id the card publishes and the challenge names.</summary>
    public const string SecuritySchemeId = "bearerAuth";

    /// <summary>The path the extended agent card is served on, relative to a binding's prefix.</summary>
    public const string ExtendedCardPath = "/extendedAgentCard";

    private const string WellKnownAgentCardPath = "/.well-known/agent-card.json";

    /// <summary>Whether the ACTS runner asked this process to enforce credentials.</summary>
    public static bool Enforced =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ITK_ACTS_AUTH"));

    /// <summary>
    /// The schemes the card publishes, or null when enforcement is off.
    /// </summary>
    /// <remarks>
    /// Declared only when the agent actually enforces: a card claiming a scheme it does not
    /// check would be a lie, and this is what the ACTS authentication precondition reads to
    /// decide whether the SEC-AUTH tests apply at all.
    /// </remarks>
    public static Dictionary<string, SecurityScheme>? SecuritySchemes() => Enforced
        ? new Dictionary<string, SecurityScheme>
        {
            [SecuritySchemeId] = new()
            {
                HttpAuthSecurityScheme = new HttpAuthSecurityScheme
                {
                    Description = "Bearer token presented by the ACTS runner.",
                    Scheme = "Bearer",
                    BearerFormat = "opaque",
                },
            },
        }
        : null;

    /// <summary>
    /// The requirements the card publishes, or null when enforcement is off.
    /// </summary>
    /// <remarks>
    /// Separate from the schemes because the two mean different things: schemes are what a
    /// client may use, requirements are what it must. An agent publishing the first and not
    /// the second requires nothing.
    /// </remarks>
    public static List<SecurityRequirement>? SecurityRequirements() => Enforced
        ? [new SecurityRequirement { Schemes = new Dictionary<string, StringList> { [SecuritySchemeId] = new() } }]
        : null;

    /// <summary>How a presented credential should be refused, or null when it passes.</summary>
    /// <remarks>
    /// Three outcomes, because the tests distinguish them: the valid token authorizes, the
    /// insufficient one authenticates but does not, and anything else — including nothing at
    /// all — fails authentication.
    /// </remarks>
    public static (int Status, string Reason, string Message)? Rejection(string? authorizationHeader)
        => PresentedToken(authorizationHeader) switch
        {
            ValidToken => null,
            InsufficientToken => (StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Token lacks the required scope."),
            _ => (StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "A bearer token is required."),
        };

    private static string? PresentedToken(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var separator = header.IndexOf(' ');
        if (separator < 0 || !header.AsSpan(0, separator).Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return header[(separator + 1)..].Trim();
    }

    /// <summary>The google.rpc.Status shape A2A §11.6 requires of an error body.</summary>
    public static JsonObject StatusBody(int code, string reason, string message) => new()
    {
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["status"] = reason,
            ["message"] = message,
            ["details"] = new JsonArray(new JsonObject
            {
                ["@type"] = "type.googleapis.com/google.rpc.ErrorInfo",
                ["reason"] = reason,
                ["domain"] = "a2a-protocol.org",
            }),
        },
    };

    /// <summary>
    /// Guards the operation endpoints when enforcement is on, and the extended card always.
    /// </summary>
    /// <remarks>
    /// The public agent card stays reachable without a credential in either mode: A2A §8.2
    /// makes the well-known URL the discovery mechanism and §7.3 has the client learn which
    /// schemes it needs from that card, so requiring one to read it would be circular — and
    /// the ITK readiness probe fetches it too.
    /// </remarks>
    public static IApplicationBuilder UseActsCredentials(this IApplicationBuilder app)
    {
        var enforced = Enforced;

        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var guarded = enforced
                ? !path.EndsWith(WellKnownAgentCardPath, StringComparison.Ordinal)
                // Suffix, not equality: the REST handlers are mounted under prefixes,
                // so the extended card answers on more than one path.
                : path.EndsWith(ExtendedCardPath, StringComparison.Ordinal);

            if (!guarded)
            {
                await next(context);
                return;
            }

            var rejection = Rejection(context.Request.Headers.Authorization);
            if (rejection is null)
            {
                await next(context);
                return;
            }

            var (status, reason, message) = rejection.Value;
            if (status == StatusCodes.Status401Unauthorized)
            {
                context.Response.Headers.WWWAuthenticate = $"Bearer realm=\"a2a\", scheme=\"{SecuritySchemeId}\"";
            }

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(StatusBody(status, reason, message).ToJsonString(JsonSerializerOptions.Default));
        });
    }
}
