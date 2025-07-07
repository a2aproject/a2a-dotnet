using System.Text.Json;

namespace A2A.UnitTests.Models;

public class AgentTransportTests
{
    [Fact]
    public void Constructor_SetsLabelCorrectly()
    {
        // Arrange & Act
        var sut = new AgentTransport("JSONRPC");

        // Assert
        Assert.Equal("JSONRPC", sut.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsOnNullOrWhitespace(string? label)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new AgentTransport(label!));
    }

    [Fact]
    public void Equality_Works_CaseInsensitive()
    {
        // Arrange
        var sut = new AgentTransport("jsonrpc");
        var other = new AgentTransport("JSONRPC");

        // Act & Assert
        Assert.True(sut == other);
        Assert.False(sut != other);
        Assert.True(sut.Equals(other));
        Assert.True(sut.Equals((object)other));
        Assert.Equal(sut.GetHashCode(), other.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsLabel()
    {
        // Arrange
        var sut = new AgentTransport("HTTP+JSON");

        // Act
        var result = sut.ToString();

        // Assert
        Assert.Equal("HTTP+JSON", result);
    }

    [Fact]
    public void JsonRpc_StaticProperty_ReturnsExpected()
    {
        // Act
        var sut = AgentTransport.JsonRpc;

        // Assert
        Assert.Equal("JSONRPC", sut.Label);
    }

    [Fact]
    public void CanSerializeAndDeserialize()
    {
        // Arrange
        var sut = new AgentTransport("GRPC");

        // Act
        var json = JsonSerializer.Serialize(sut);
        var deserialized = JsonSerializer.Deserialize<AgentTransport>(json);

        // Assert
        Assert.Equal(sut, deserialized);
        Assert.Equal("GRPC", deserialized!.Label);
    }

    [Fact]
    public void SerializesToSimpleString()
    {
        // Arrange
        var sut = new AgentTransport("JSONRPC");

        // Act
        var json = JsonSerializer.Serialize(sut);

        // Assert
        Assert.Equal("\"JSONRPC\"", json); // Should be a simple quoted string, not an object
    }

    [Fact]
    public void SerializesCorrectlyWithinAgentInterface()
    {
        // Arrange
        var agentInterface = new AgentInterface
        {
            Transport = new AgentTransport("GRPC"),
            Url = "https://example.com/agent"
        };

        // Act
        var json = JsonSerializer.Serialize(agentInterface);

        // Assert
        // Should contain "transport":"GRPC", not "transport":{"transport":"GRPC"}
        Assert.Contains("\"transport\":\"GRPC\"", json);
        Assert.DoesNotContain("\"transport\":{\"transport\":", json);
    }
}
