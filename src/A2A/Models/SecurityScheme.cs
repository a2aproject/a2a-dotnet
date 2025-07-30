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
/// <remarks>
/// Initializes a new instance of the <see cref="SecurityScheme"/> class.
/// </remarks>
/// <param name="description">A short description for the security scheme. CommonMark syntax MAY be used for rich text representation.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ApiKeySecurityScheme), "apiKey")]
[JsonDerivedType(typeof(HttpAuthSecurityScheme), "http")]
[JsonDerivedType(typeof(OAuth2SecurityScheme), "oauth2")]
[JsonDerivedType(typeof(OpenIdConnectSecurityScheme), "openIdConnect")]
[JsonDerivedType(typeof(MutualTlsSecurityScheme), "mutualTLS")]
public abstract class SecurityScheme(string? description = null)
{
    /// <summary>
    /// A short description for security scheme. CommonMark syntax MAY be used for rich text representation.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; } = description;
}

/// <summary>
/// API Key security scheme.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ApiKeySecurityScheme"/> class.
/// </remarks>
/// <param name="name">The name of the header, query or cookie parameter to be used.</param>
/// <param name="keyLocation">The location of the API key. Valid values are "query", "header", or "cookie".</param>
/// <param name="description">
/// A short description for the security scheme. CommonMark syntax MAY be used for rich text representation.
/// </param>
public sealed class ApiKeySecurityScheme(string name, string keyLocation, string? description = "API key for authentication") : SecurityScheme(description)
{
    /// <summary>
    /// The name of the header, query or cookie parameter to be used.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonRequired]
    public string Name { get; init; } = name;

    /// <summary>
    /// The location of the API key. Valid values are "query", "header", or "cookie".
    /// </summary>
    [JsonPropertyName("in")]
    [JsonRequired]
    public string KeyLocation { get; init; } = keyLocation;
}

/// <summary>
/// HTTP Authentication security scheme.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="HttpAuthSecurityScheme"/> class.
/// </remarks>
/// <param name="scheme">The name of the HTTP Authentication scheme to be used in the Authorization header as defined in RFC7235.</param>
/// <param name="bearerFormat">A hint to the client to identify how the bearer token is formatted.</param>
/// <param name="description">A short description for the security scheme. CommonMark syntax MAY be used for rich text representation.</param>
public sealed class HttpAuthSecurityScheme(string scheme, string? bearerFormat = null, string? description = null) : SecurityScheme(description)
{
    /// <summary>
    /// The name of the HTTP Authentication scheme to be used in the Authorization header as defined in RFC7235.
    /// </summary>
    [JsonPropertyName("scheme")]
    [JsonRequired]
    public string Scheme { get; init; } = scheme;

    /// <summary>
    /// A hint to the client to identify how the bearer token is formatted.
    /// </summary>
    [JsonPropertyName("bearerFormat")]
    public string? BearerFormat { get; init; } = bearerFormat;
}

/// <summary>
/// OAuth2.0 security scheme configuration.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="OAuth2SecurityScheme"/> class.
/// </remarks>
/// <param name="flows">An object containing configuration information for the flow types supported.</param>
/// <param name="description">A short description for the security scheme. CommonMark syntax MAY be used for rich text representation.</param>
public sealed class OAuth2SecurityScheme(OAuthFlows flows, string? description = null) : SecurityScheme(description)
{
    /// <summary>
    /// An object containing configuration information for the flow types supported.
    /// </summary>
    [JsonPropertyName("flows")]
    [JsonRequired]
    public OAuthFlows Flows { get; init; } = flows;
}

/// <summary>
/// OpenID Connect security scheme configuration.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="OpenIdConnectSecurityScheme"/> class.
/// </remarks>
/// <param name="openIdConnectUrl">Well-known URL to discover the [[OpenID-Connect-Discovery]] provider metadata.</param>
/// <param name="description">A short description for the security scheme. CommonMark syntax MAY be used for rich text representation.</param>
public sealed class OpenIdConnectSecurityScheme(Uri openIdConnectUrl, string? description = null) : SecurityScheme(description)
{
    /// <summary>
    /// Well-known URL to discover the [[OpenID-Connect-Discovery]] provider metadata.
    /// </summary>
    [JsonPropertyName("openIdConnectUrl")]
    [JsonRequired]
    public Uri OpenIdConnectUrl { get; init; } = openIdConnectUrl;
}

/// <summary>
/// Mutual TLS security scheme configuration.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MutualTlsSecurityScheme"/> class.
/// </remarks>
/// <param name="description">A short description for the security scheme. CommonMark syntax MAY be used for rich text representation.</param>
public sealed class MutualTlsSecurityScheme(string? description = "Mutual TLS authentication") : SecurityScheme(description)
{
}

/// <summary>
/// Allows configuration of the supported OAuth Flows.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="OAuthFlows"/> class.
/// </remarks>
public sealed class OAuthFlows
{
    /// <summary>
    /// Configuration for the OAuth Authorization Code flow. Previously called accessCode in OpenAPI 2.0.
    /// </summary>
    [JsonPropertyName("authorizationCode")]
    public AuthorizationCodeOAuthFlow? AuthorizationCode { get; init; }

    /// <summary>
    /// Configuration for the OAuth Client Credentials flow. Previously called application in OpenAPI 2.0.
    /// </summary>
    [JsonPropertyName("clientCredentials")]
    public ClientCredentialsOAuthFlow? ClientCredentials { get; init; }

    /// <summary>
    /// Configuration for the OAuth Resource Owner Password flow.
    /// </summary>
    [JsonPropertyName("password")]
    public PasswordOAuthFlow? Password { get; init; }

    /// <summary>
    /// Configuration for the OAuth Implicit flow.
    /// </summary>
    [JsonPropertyName("implicit")]
    public ImplicitOAuthFlow? Implicit { get; init; }
}

/// <summary>
/// Configuration details applicable to all OAuth Flows.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BaseOauthFlow"/> class.
/// </remarks>
/// <param name="scopes">
/// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
/// </param>
/// <param name="refreshUrl">
/// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
public abstract class BaseOauthFlow(IDictionary<string, string> scopes,
    Uri? refreshUrl = null)
{
    /// <summary>
    /// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
    /// </summary>
    [JsonPropertyName("refreshUrl")]
    public Uri? RefreshUrl { get; init; } = refreshUrl;

    /// <summary>
    /// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
    /// </summary>
    [JsonPropertyName("scopes")]
    [JsonRequired]
    public IDictionary<string, string> Scopes { get; init; } = scopes;
}

/// <summary>
/// Configuration details for an OAuth Flow requiring a Token URL.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TokenUrlOauthFlow"/> class.
/// </remarks>
/// <param name="tokenUrl">
/// The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
/// <param name="scopes">
/// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
/// </param>
/// <param name="refreshUrl">
/// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
public abstract class TokenUrlOauthFlow(Uri tokenUrl,
    IDictionary<string, string> scopes,
    Uri? refreshUrl = null) : BaseOauthFlow(scopes, refreshUrl)
{
    /// <summary>
    /// The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
    /// </summary>
    [JsonPropertyName("tokenUrl")]
    [JsonRequired]
    public Uri TokenUrl { get; init; } = tokenUrl;
}

/// <summary>
/// Configuration details for an OAuth Flow requiring an Authorization URL.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AuthUrlOauthFlow"/> class.
/// </remarks>
/// <param name="authorizationUrl">
/// The authorization URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
/// <param name="scopes">
/// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
/// </param>
/// <param name="refreshUrl">
/// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
public abstract class AuthUrlOauthFlow(Uri authorizationUrl,
    IDictionary<string, string> scopes,
    Uri? refreshUrl = null) : BaseOauthFlow(scopes, refreshUrl)
{
    /// <summary>
    /// The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
    /// </summary>
    [JsonPropertyName("authorizationUrl")]
    [JsonRequired]
    public Uri AuthorizationUrl { get; init; } = authorizationUrl;
}

/// <summary>
/// Configuration details for a supported OAuth Authorization Code Flow.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AuthorizationCodeOAuthFlow"/> class.
/// </remarks>
/// <param name="authorizationUrl">
/// The authorization URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
/// <param name="tokenUrl">
/// The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
/// <param name="scopes">
/// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
/// </param>
/// <param name="refreshUrl">
/// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
public sealed class AuthorizationCodeOAuthFlow(
    Uri authorizationUrl,
    Uri tokenUrl,
    IDictionary<string, string> scopes,
    Uri? refreshUrl = null) : TokenUrlOauthFlow(tokenUrl, scopes, refreshUrl)
{
    /// <summary>
    /// The authorization URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
    /// </summary>
    [JsonPropertyName("authorizationUrl")]
    [JsonRequired]
    public Uri AuthorizationUrl { get; init; } = authorizationUrl;
}

/// <summary>
/// Configuration details for a supported OAuth Client Credentials Flow.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ClientCredentialsOAuthFlow"/> class.
/// </remarks>
/// <param name="tokenUrl">
/// The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
/// <param name="scopes">
/// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
/// </param>
/// <param name="refreshUrl">
/// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
public sealed class ClientCredentialsOAuthFlow(
    Uri tokenUrl,
    IDictionary<string, string> scopes,
    Uri? refreshUrl = null) : TokenUrlOauthFlow(tokenUrl, scopes, refreshUrl)
{ }

/// <summary>
/// Configuration details for a supported OAuth Resource Owner Password Flow.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PasswordOAuthFlow"/> class.
/// </remarks>
/// <param name="tokenUrl">
/// The token URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
/// <param name="scopes">
/// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
/// </param>
/// <param name="refreshUrl">
/// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
public sealed class PasswordOAuthFlow(
    Uri tokenUrl,
    IDictionary<string, string> scopes,
    Uri? refreshUrl = null) : TokenUrlOauthFlow(tokenUrl, scopes, refreshUrl)
{ }

/// <summary>
/// Configuration details for a supported OAuth Implicit Flow.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ImplicitOAuthFlow"/> class.
/// </remarks>
/// <param name="authorizationUrl">
/// The authorization URL to be used for this flow. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
/// <param name="scopes">
/// The available scopes for the OAuth2 security scheme. A map between the scope name and a short description for it. The map MAY be empty.
/// </param>
/// <param name="refreshUrl">
/// The URL to be used for obtaining refresh tokens. This MUST be in the form of a URL. The OAuth2 standard requires the use of TLS.
/// </param>
public sealed class ImplicitOAuthFlow(
    Uri authorizationUrl,
    IDictionary<string, string> scopes,
    Uri? refreshUrl = null) : AuthUrlOauthFlow(authorizationUrl, scopes, refreshUrl)
{ }