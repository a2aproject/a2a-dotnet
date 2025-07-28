using System.Text.Json;

namespace A2A.UnitTests.Models;

public class SecuritySchemeTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void ApiKeySecurityScheme_HasCorrectType()
    {
        // Arrange & Act
        var scheme = new ApiKeySecurityScheme
        {
            Name = "X-API-Key",
            In = "header"
        };

        // Assert
        Assert.Equal("apiKey", scheme.Type);
    }

    [Fact]
    public void HttpAuthSecurityScheme_HasCorrectType()
    {
        // Arrange & Act
        var scheme = new HttpAuthSecurityScheme
        {
            Scheme = "bearer"
        };

        // Assert
        Assert.Equal("http", scheme.Type);
    }

    [Fact]
    public void OAuth2SecurityScheme_HasCorrectType()
    {
        // Arrange & Act
        var scheme = new OAuth2SecurityScheme();

        // Assert
        Assert.Equal("oauth2", scheme.Type);
    }

    [Fact]
    public void OpenIdConnectSecurityScheme_HasCorrectType()
    {
        // Arrange & Act
        var scheme = new OpenIdConnectSecurityScheme
        {
            OpenIdConnectUrl = "https://example.com/.well-known/openid_configuration"
        };

        // Assert
        Assert.Equal("openIdConnect", scheme.Type);
    }

    [Fact]
    public void MutualTlsSecurityScheme_HasCorrectType()
    {
        // Arrange & Act
        var scheme = new MutualTlsSecurityScheme();

        // Assert
        Assert.Equal("mutualTLS", scheme.Type);
    }

    [Fact]
    public void SecurityScheme_DescriptionProperty_SerializesCorrectly()
    {
        // Arrange
        var scheme = new ApiKeySecurityScheme
        {
            Name = "X-API-Key",
            In = "header",
            Description = "API key for authentication"
        };

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ApiKeySecurityScheme>(json, s_jsonOptions);

        // Assert
        Assert.Contains("\"description\": \"API key for authentication\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal("API key for authentication", deserialized.Description);
    }

    [Fact]
    public void SecurityScheme_DescriptionProperty_CanBeNull()
    {
        // Arrange
        var scheme = new HttpAuthSecurityScheme
        {
            Scheme = "bearer",
            Description = null
        };

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<HttpAuthSecurityScheme>(json, s_jsonOptions);

        // Assert
        Assert.DoesNotContain("\"description\"", json);
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Description);
    }

    [Fact]
    public void MutualTlsSecurityScheme_SerializesCorrectly()
    {
        // Arrange
        var scheme = new MutualTlsSecurityScheme
        {
            Description = "Mutual TLS authentication"
        };

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<MutualTlsSecurityScheme>(json, s_jsonOptions);

        // Assert
        Assert.Contains("\"type\": \"mutualTLS\"", json);
        Assert.Contains("\"description\": \"Mutual TLS authentication\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal("mutualTLS", deserialized.Type);
        Assert.Equal("Mutual TLS authentication", deserialized.Description);
    }

    [Fact]
    public void AllSecuritySchemeTypes_HaveCorrectTypeValues()
    {
        // Arrange & Act
        var apiKey = new ApiKeySecurityScheme { Name = "key", In = "header" };
        var http = new HttpAuthSecurityScheme { Scheme = "bearer" };
        var oauth2 = new OAuth2SecurityScheme();
        var openId = new OpenIdConnectSecurityScheme { OpenIdConnectUrl = "https://example.com/.well-known/openid_configuration" };
        var mutualTls = new MutualTlsSecurityScheme();

        // Assert - Verify all types are set correctly
        Assert.Equal("apiKey", apiKey.Type);
        Assert.Equal("http", http.Type);
        Assert.Equal("oauth2", oauth2.Type);
        Assert.Equal("openIdConnect", openId.Type);
        Assert.Equal("mutualTLS", mutualTls.Type);
    }

    [Fact]
    public void AllSecuritySchemeTypes_CanHaveDescription()
    {
        // Arrange
        var schemes = new SecurityScheme[]
        {
            new ApiKeySecurityScheme { Name = "key", In = "header", Description = "API key auth" },
            new HttpAuthSecurityScheme { Scheme = "bearer", Description = "HTTP auth" },
            new OAuth2SecurityScheme { Description = "OAuth 2.0 auth" },
            new OpenIdConnectSecurityScheme { OpenIdConnectUrl = "https://example.com/.well-known/openid_configuration", Description = "OIDC auth" },
            new MutualTlsSecurityScheme { Description = "mTLS auth" }
        };

        // Act & Assert
        foreach (var scheme in schemes)
        {
            var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
            Assert.Contains("\"description\":", json);
            Assert.NotNull(scheme.Description);
        }
    }
}