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
        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Description);
    }

    [Fact]
    public void MutualTlsSecurityScheme_SerializesCorrectly()
    {
        // Arrange
        SecurityScheme scheme = new MutualTlsSecurityScheme();

        // Act
        var json = JsonSerializer.Serialize(scheme, s_jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json, s_jsonOptions) as MutualTlsSecurityScheme;

        // Assert
        Assert.Contains("\"type\": \"mutualTLS\"", json);
        Assert.Contains("\"description\": \"Mutual TLS authentication\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal("Mutual TLS authentication", deserialized.Description);
    }

    [Fact]
    public void ApiKeySecurityScheme_DeserializesFromBaseSecurityScheme()
    {
        // Arrange
        var json = """
            {
            "type": "apiKey",
            "name": "X-API-Key",
            "in": "header"
            }
            """;
        var deserialized = JsonSerializer.Deserialize<SecurityScheme>(json);
        Assert.IsType<ApiKeySecurityScheme>(deserialized);
    }
}