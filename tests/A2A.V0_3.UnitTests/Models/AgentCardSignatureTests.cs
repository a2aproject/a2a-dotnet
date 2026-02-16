using System.Text.Json;

namespace A2A.V0_3.UnitTests.Models;

public class AgentCardSignatureTests
{
    [Fact]
    public void AgentCardSignature_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var signature = new AgentCardSignature
        {
            Protected = "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpPU0UiLCJraWQiOiJrZXktMSIsImprdSI6Imh0dHBzOi8vZXhhbXBsZS5jb20vYWdlbnQvandrcy5qc29uIn0",
            Signature = "QFdkNLNszlGj3z3u0YQGt_T9LixY3qtdQpZmsTdDHDe3fXV9y9-B3m2-XgCpzuhiLt8E0tV6HXoZKHv4GtHgKQ",
            Header = new Dictionary<string, object>
            {
                { "kid", "key-1" },
                { "alg", "ES256" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(signature, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentCardSignature>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(signature.Protected, deserialized.Protected);
        Assert.Equal(signature.Signature, deserialized.Signature);
        Assert.NotNull(deserialized.Header);
        Assert.Equal(2, deserialized.Header.Count);
        Assert.True(deserialized.Header.ContainsKey("kid"));
        Assert.True(deserialized.Header.ContainsKey("alg"));
    }

    [Fact]
    public void AgentCardSignature_WithoutHeader_SerializesCorrectly()
    {
        // Arrange
        var signature = new AgentCardSignature
        {
            Protected = "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpPU0UiLCJraWQiOiJrZXktMSIsImprdSI6Imh0dHBzOi8vZXhhbXBsZS5jb20vYWdlbnQvandrcy5qc29uIn0",
            Signature = "QFdkNLNszlGj3z3u0YQGt_T9LixY3qtdQpZmsTdDHDe3fXV9y9-B3m2-XgCpzuhiLt8E0tV6HXoZKHv4GtHgKQ"
        };

        // Act
        var json = JsonSerializer.Serialize(signature, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AgentCardSignature>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(signature.Protected, deserialized.Protected);
        Assert.Equal(signature.Signature, deserialized.Signature);
        Assert.Null(deserialized.Header);

        // Verify JSON doesn't include null header
        Assert.DoesNotContain("\"header\"", json);
    }

    [Fact]
    public void AgentCardSignature_MatchesSpecExample()
    {
        // Arrange - JSON example from the A2A spec
        var specJson = """
        {
          "protected": "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpPU0UiLCJraWQiOiJrZXktMSIsImprdSI6Imh0dHBzOi8vZXhhbXBsZS5jb20vYWdlbnQvandrcy5qc29uIn0",
          "signature": "QFdkNLNszlGj3z3u0YQGt_T9LixY3qtdQpZmsTdDHDe3fXV9y9-B3m2-XgCpzuhiLt8E0tV6HXoZKHv4GtHgKQ"
        }
        """;

        // Act
        var deserialized = JsonSerializer.Deserialize<AgentCardSignature>(specJson, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("eyJhbGciOiJFUzI1NiIsInR5cCI6IkpPU0UiLCJraWQiOiJrZXktMSIsImprdSI6Imh0dHBzOi8vZXhhbXBsZS5jb20vYWdlbnQvandrcy5qc29uIn0", deserialized.Protected);
        Assert.Equal("QFdkNLNszlGj3z3u0YQGt_T9LixY3qtdQpZmsTdDHDe3fXV9y9-B3m2-XgCpzuhiLt8E0tV6HXoZKHv4GtHgKQ", deserialized.Signature);
        Assert.Null(deserialized.Header);
    }
}
