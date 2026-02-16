using System.Text.Json;

namespace A2A.UnitTests.Models;

public class SecuritySchemeTests
{
    [Fact]
    public void WhenApiKeyScheme_Serialized_OnlyApiKeyFieldPresent()
    {
        var scheme = new SecurityScheme
        {
            ApiKeySecurityScheme = new ApiKeySecurityScheme { Name = "api_key", Location = "header" }
        };

        var json = JsonSerializer.Serialize(scheme, A2AJsonUtilities.DefaultOptions);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("apiKeySecurityScheme", out _));
        Assert.False(doc.RootElement.TryGetProperty("httpAuthSecurityScheme", out _));
        Assert.False(doc.RootElement.TryGetProperty("oauth2SecurityScheme", out _));
    }

    [Fact]
    public void WhenSecurityScheme_RoundTrips_Correctly()
    {
        var scheme = new SecurityScheme
        {
            HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Scheme = "bearer", BearerFormat = "JWT" }
        };

        var json = JsonSerializer.Serialize(scheme, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized?.HttpAuthSecurityScheme);
        Assert.Equal("bearer", deserialized.HttpAuthSecurityScheme.Scheme);
        Assert.Equal("JWT", deserialized.HttpAuthSecurityScheme.BearerFormat);
    }

    [Fact]
    public void WhenOAuth2Scheme_RoundTrips_Correctly()
    {
        var scheme = new SecurityScheme
        {
            OAuth2SecurityScheme = new OAuth2SecurityScheme
            {
                Flows = new OAuthFlows
                {
                    ClientCredentials = new ClientCredentialsOAuthFlow
                    {
                        TokenUrl = "https://example.com/token",
                        Scopes = new Dictionary<string, string> { ["read"] = "Read access" },
                    },
                }
            }
        };

        var json = JsonSerializer.Serialize(scheme, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized?.OAuth2SecurityScheme);
        Assert.NotNull(deserialized.OAuth2SecurityScheme.Flows?.ClientCredentials);
        Assert.Equal("https://example.com/token", deserialized.OAuth2SecurityScheme.Flows.ClientCredentials.TokenUrl);
    }

    [Fact]
    public void WhenEmptySecurityScheme_Serialized_OmitsAllNullFields()
    {
        var scheme = new SecurityScheme();

        var json = JsonSerializer.Serialize(scheme, A2AJsonUtilities.DefaultOptions);
        var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("apiKeySecurityScheme", out _));
        Assert.False(doc.RootElement.TryGetProperty("httpAuthSecurityScheme", out _));
        Assert.False(doc.RootElement.TryGetProperty("oauth2SecurityScheme", out _));
        Assert.False(doc.RootElement.TryGetProperty("openIdConnectSecurityScheme", out _));
        Assert.False(doc.RootElement.TryGetProperty("mtlsSecurityScheme", out _));
    }
}