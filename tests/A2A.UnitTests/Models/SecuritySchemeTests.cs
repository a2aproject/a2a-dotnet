using System.Text.Json;

namespace A2A.UnitTests.Models;

public class SecuritySchemeTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void SecurityScheme_DescriptionProperty_SerializesCorrectly()
    {
        // Arrange
        SecurityScheme scheme = new ApiKeySecurityScheme("X-API-Key", "header");

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, s_jsonOptions) as ApiKeySecurityScheme;

        // Assert
        Assert.Contains("\"type\": \"apiKey\"", json);
        Assert.Contains("\"description\": \"API key for authentication\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal("API key for authentication", deserialized.Description);
    }

    [Fact]
    public void SecurityScheme_DescriptionProperty_CanBeNull()
    {
        // Arrange
        SecurityScheme scheme = new HttpAuthSecurityScheme("bearer", null);

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, s_jsonOptions) as HttpAuthSecurityScheme;

        // Assert
        Assert.DoesNotContain("\"description\"", json);
        Assert.Contains("\"type\": \"http\"", json);
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Description);
    }

    [Fact]
    public void ApiKeySecurityScheme_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        SecurityScheme scheme = new ApiKeySecurityScheme("X-API-Key", "header");

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, s_jsonOptions) as ApiKeySecurityScheme;

        // Assert
        Assert.Contains("\"type\": \"apiKey\"", json);
        Assert.Contains("\"description\":", json);
        Assert.NotNull(deserialized);
        Assert.Equal("API key for authentication", deserialized.Description);
        Assert.Equal("X-API-Key", deserialized.Name);
        Assert.Equal("header", deserialized.KeyLocation);
    }

    [Fact]
    public void HttpAuthSecurityScheme_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        SecurityScheme scheme = new HttpAuthSecurityScheme("bearer");

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, s_jsonOptions) as HttpAuthSecurityScheme;

        // Assert
        Assert.Contains("\"type\": \"http\"", json);
        Assert.DoesNotContain("\"description\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal("bearer", deserialized.Scheme);
        Assert.Null(deserialized.Description);
    }

    [Fact]
    public void OAuth2SecurityScheme_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var flows = new OAuthFlows
        {
            Password = new(new("https://example.com/token"), scopes: new Dictionary<string, string>() { ["read"] = "Read access", ["write"] = "Write access" }),
        };
        SecurityScheme scheme = new OAuth2SecurityScheme(flows, "OAuth2 authentication");

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, s_jsonOptions) as OAuth2SecurityScheme;

        // Assert
        Assert.Contains("\"type\": \"oauth2\"", json);
        Assert.Contains("\"description\": \"OAuth2 authentication\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal("OAuth2 authentication", deserialized.Description);
        Assert.NotNull(deserialized.Flows);
        Assert.Null(deserialized.Flows.ClientCredentials);
        Assert.Null(deserialized.Flows.Implicit);
        Assert.Null(deserialized.Flows.AuthorizationCode);
        Assert.NotNull(deserialized.Flows.Password);
        Assert.Equal("https://example.com/token", deserialized.Flows.Password.TokenUrl.ToString());
        Assert.NotNull(deserialized.Flows.Password.Scopes);
        Assert.Equal(2, deserialized.Flows.Password.Scopes.Count);
        Assert.Contains("read", deserialized.Flows.Password.Scopes.Keys);
        Assert.Contains("write", deserialized.Flows.Password.Scopes.Keys);
        Assert.Equal("Read access", deserialized.Flows.Password.Scopes["read"]);
        Assert.Equal("Write access", deserialized.Flows.Password.Scopes["write"]);
    }

    [Fact]
    public void OpenIdConnectSecurityScheme_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        SecurityScheme scheme = new OpenIdConnectSecurityScheme(new("https://example.com/.well-known/openid_configuration"), "OpenID Connect authentication");

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, s_jsonOptions) as OpenIdConnectSecurityScheme;

        // Assert
        Assert.Contains("\"type\": \"openIdConnect\"", json);
        Assert.Contains("\"description\": \"OpenID Connect authentication\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal("OpenID Connect authentication", deserialized.Description);
        Assert.Equal("https://example.com/.well-known/openid_configuration", deserialized.OpenIdConnectUrl.ToString());
    }

    [Fact]
    public void MutualTlsSecurityScheme_DeserializesFromBaseSecurityScheme()
    {
        // Arrange
        SecurityScheme scheme = new MutualTlsSecurityScheme();

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json);

        // Assert
        Assert.IsType<MutualTlsSecurityScheme>(deserialized);
        Assert.Equal("Mutual TLS authentication", deserialized.Description);
    }
}