namespace A2A;

using System.Text.Json.Serialization;

/// <summary>Identifies which OAuth flow is set.</summary>
public enum OAuthFlowCase
{
    /// <summary>No flow is set.</summary>
    None,
    /// <summary>Authorization code flow.</summary>
    AuthorizationCode,
    /// <summary>Client credentials flow.</summary>
    ClientCredentials,
    /// <summary>Implicit flow (deprecated).</summary>
    Implicit,
    /// <summary>Password flow (deprecated).</summary>
    Password,
    /// <summary>Device code flow.</summary>
    DeviceCode,
}

/// <summary>Represents OAuth2 flow configurations. Uses field-presence to indicate which flows are available.</summary>
public sealed class OAuthFlows
{
    /// <summary>Gets or sets the authorization code flow.</summary>
    public AuthorizationCodeOAuthFlow? AuthorizationCode { get; set; }

    /// <summary>Gets or sets the client credentials flow.</summary>
    public ClientCredentialsOAuthFlow? ClientCredentials { get; set; }

    /// <summary>Gets or sets the implicit flow.</summary>
    [Obsolete("Implicit flow is deprecated.")]
    public ImplicitOAuthFlow? Implicit { get; set; }

    /// <summary>Gets or sets the password flow.</summary>
    [Obsolete("Password flow is deprecated.")]
    public PasswordOAuthFlow? Password { get; set; }

    /// <summary>Gets or sets the device code flow.</summary>
    public DeviceCodeOAuthFlow? DeviceCode { get; set; }

    /// <summary>Gets which OAuth flow is currently set.</summary>
    [JsonIgnore]
    public OAuthFlowCase FlowCase =>
        AuthorizationCode is not null ? OAuthFlowCase.AuthorizationCode :
        ClientCredentials is not null ? OAuthFlowCase.ClientCredentials :
#pragma warning disable CS0618 // Obsolete members must still be inspected
        Implicit is not null ? OAuthFlowCase.Implicit :
        Password is not null ? OAuthFlowCase.Password :
#pragma warning restore CS0618
        DeviceCode is not null ? OAuthFlowCase.DeviceCode :
        OAuthFlowCase.None;
}
