namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Identifies which security scheme type is set.</summary>
public enum SecuritySchemeCase
{
    /// <summary>No scheme is set.</summary>
    None,
    /// <summary>API key security scheme.</summary>
    ApiKey,
    /// <summary>HTTP auth security scheme.</summary>
    HttpAuth,
    /// <summary>OAuth2 security scheme.</summary>
    OAuth2,
    /// <summary>OpenID Connect security scheme.</summary>
    OpenIdConnect,
    /// <summary>Mutual TLS security scheme.</summary>
    Mtls,
}

/// <summary>Represents a security scheme. Uses field-presence to indicate the scheme type.</summary>
public sealed class SecurityScheme
{
    /// <summary>Gets or sets the API key security scheme.</summary>
    [JsonPropertyName("apiKeySecurityScheme")]
    public ApiKeySecurityScheme? ApiKeySecurityScheme { get; set; }

    /// <summary>Gets or sets the HTTP auth security scheme.</summary>
    [JsonPropertyName("httpAuthSecurityScheme")]
    public HttpAuthSecurityScheme? HttpAuthSecurityScheme { get; set; }

    /// <summary>Gets or sets the OAuth2 security scheme.</summary>
    [JsonPropertyName("oauth2SecurityScheme")]
    public OAuth2SecurityScheme? OAuth2SecurityScheme { get; set; }

    /// <summary>Gets or sets the OpenID Connect security scheme.</summary>
    [JsonPropertyName("openIdConnectSecurityScheme")]
    public OpenIdConnectSecurityScheme? OpenIdConnectSecurityScheme { get; set; }

    /// <summary>Gets or sets the mutual TLS security scheme.</summary>
    [JsonPropertyName("mtlsSecurityScheme")]
    public MutualTlsSecurityScheme? MtlsSecurityScheme { get; set; }

    /// <summary>Gets which security scheme type is currently set.</summary>
    [JsonIgnore]
    public SecuritySchemeCase SchemeCase =>
        ApiKeySecurityScheme is not null ? SecuritySchemeCase.ApiKey :
        HttpAuthSecurityScheme is not null ? SecuritySchemeCase.HttpAuth :
        OAuth2SecurityScheme is not null ? SecuritySchemeCase.OAuth2 :
        OpenIdConnectSecurityScheme is not null ? SecuritySchemeCase.OpenIdConnect :
        MtlsSecurityScheme is not null ? SecuritySchemeCase.Mtls :
        SecuritySchemeCase.None;
}