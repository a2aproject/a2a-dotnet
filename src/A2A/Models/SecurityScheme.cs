using System.Text.Json.Serialization;

namespace A2A;

/// <summary>
/// Mirrors the OpenAPI Security Scheme Object.
/// (https://swagger.io/specification/#security-scheme-object)
/// </summary>
/// <remarks>
/// This is the base type for all supported OpenAPI security schemes.
/// The <c>type</c> property is used as a discriminator for polymorphic deserialization.
/// </remarks>
/// <param name="Description">A short description for security scheme. CommonMark syntax MAY be used for rich text representation.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ApiKeySecurityScheme), "apiKey")]
[JsonDerivedType(typeof(HttpAuthSecurityScheme), "http")]
[JsonDerivedType(typeof(OAuth2SecurityScheme), "oauth2")]
[JsonDerivedType(typeof(OpenIdConnectSecurityScheme), "openIdConnect")]
[JsonDerivedType(typeof(MutualTlsSecurityScheme), "mutualTLS")]
public abstract record SecurityScheme(
    [property: JsonPropertyName("description")] string? Description = null);

/// <summary>
/// API Key security scheme.
/// </summary>
/// <param name="Name">The name of the header, query or cookie parameter to be used.</param>
/// <param name="In">The location of the API key. Valid values are "query", "header", or "cookie".</param>
/// <param name="Description">A short description for security scheme. Optional.</param>
public sealed record ApiKeySecurityScheme(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("in"), JsonRequired] string In,
    string? Description = "API key for authentication"
) : SecurityScheme(Description);

/// <summary>
/// HTTP Authentication security scheme.
/// </summary>
/// <param name="Scheme">The name of the HTTP Authentication scheme to be used in the Authorization header as defined in RFC7235.</param>
/// <param name="BearerFormat">A hint to the client to identify how the bearer token is formatted.</param>
/// <param name="Description">A short description for security scheme. Optional.</param>
public sealed record HttpAuthSecurityScheme(
    [property: JsonPropertyName("scheme"), JsonRequired] string Scheme,
    [property: JsonPropertyName("bearerFormat")] string? BearerFormat = null,
    string? Description = null
) : SecurityScheme(Description);

/// <summary>
/// OAuth2.0 security scheme configuration.
/// </summary>
/// <param name="Flows">An object containing configuration information for the flow types supported.</param>
/// <param name="Description">A short description for security scheme. Optional.</param>
public sealed record OAuth2SecurityScheme(
    [property: JsonPropertyName("flows"), JsonRequired] OAuthFlows Flows,
    string? Description = null
) : SecurityScheme(Description);

/// <summary>
/// OpenID Connect security scheme configuration.
/// </summary>
/// <param name="OpenIdConnectUrl">Well-known URL to discover the [[OpenID-Connect-Discovery]] provider metadata.</param>
/// <param name="Description">A short description for security scheme. Optional.</param>
public sealed record OpenIdConnectSecurityScheme(
    [property: JsonPropertyName("openIdConnectUrl"), JsonRequired] string OpenIdConnectUrl,
    string? Description = null
) : SecurityScheme(Description);

/// <summary>
/// Mutual TLS security scheme configuration.
/// </summary>
/// <param name="Description">A short description for security scheme. Optional.</param>
public sealed record MutualTlsSecurityScheme(
    string? Description = "Mutual TLS authentication"
) : SecurityScheme(Description);

/// <summary>
/// Allows configuration of the supported OAuth Flows.
/// </summary>
/// <param name="AuthorizationCode">Configuration for the OAuth Authorization Code flow. Previously called accessCode in OpenAPI 2.0.</param>
/// <param name="ClientCredentials">Configuration for the OAuth Client Credentials flow. Previously called application in OpenAPI 2.0.</param>
/// <param name="Password">Configuration for the OAuth Resource Owner Password flow.</param>
/// <param name="Implicit">Configuration for the OAuth Implicit flow.</param>
public sealed record OAuthFlows(
    [property: JsonPropertyName("authorizationCode")] AuthorizationCodeOAuthFlow? AuthorizationCode = null,
    [property: JsonPropertyName("clientCredentials")] ClientCredentialsOAuthFlow? ClientCredentials = null,
    [property: JsonPropertyName("password")] PasswordOAuthFlow? Password = null,
    [property: JsonPropertyName("implicit")] ImplicitOAuthFlow? Implicit = null
);

/// <summary>
/// Configuration details for a supported OAuth Authorization Code Flow.
/// </summary>
/// <param name="AuthorizationUrl">The authorization URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="TokenUrl">The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="RefreshUrl">The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="Scopes">The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.</param>
public sealed record AuthorizationCodeOAuthFlow(
    [property: JsonPropertyName("authorizationUrl"), JsonRequired] string AuthorizationUrl,
    [property: JsonPropertyName("tokenUrl"), JsonRequired] string TokenUrl,
    [property: JsonPropertyName("refreshUrl")] string? RefreshUrl = null,
    [property: JsonPropertyName("scopes"), JsonRequired] Dictionary<string, string> Scopes = null!
);

/// <summary>
/// Configuration details for a supported OAuth Client Credentials Flow.
/// </summary>
/// <param name="TokenUrl">The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="RefreshUrl">The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="Scopes">The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.</param>
public sealed record ClientCredentialsOAuthFlow(
    [property: JsonPropertyName("tokenUrl"), JsonRequired] string TokenUrl,
    [property: JsonPropertyName("refreshUrl")] string? RefreshUrl = null,
    [property: JsonPropertyName("scopes"), JsonRequired] Dictionary<string, string> Scopes = null!
);

/// <summary>
/// Configuration details for a supported OAuth Resource Owner Password Flow.
/// </summary>
/// <param name="TokenUrl">The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="RefreshUrl">The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="Scopes">The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.</param>
public sealed record PasswordOAuthFlow(
    [property: JsonPropertyName("tokenUrl"), JsonRequired] string TokenUrl,
    [property: JsonPropertyName("refreshUrl")] string? RefreshUrl = null,
    [property: JsonPropertyName("scopes"), JsonRequired] Dictionary<string, string> Scopes = null!
);

/// <summary>
/// Configuration details for a supported OAuth Implicit Flow.
/// </summary>
/// <param name="AuthorizationUrl">The authorization URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="RefreshUrl">The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.</param>
/// <param name="Scopes">The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.</param>
public sealed record ImplicitOAuthFlow(
    [property: JsonPropertyName("authorizationUrl"), JsonRequired] string AuthorizationUrl,
    [property: JsonPropertyName("refreshUrl")] string? RefreshUrl = null,
    [property: JsonPropertyName("scopes"), JsonRequired] Dictionary<string, string> Scopes = null!
);