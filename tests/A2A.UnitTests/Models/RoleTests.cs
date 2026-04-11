using System.Text.Json;

namespace A2A.UnitTests.Models;

public sealed class RoleTests
{
    [Fact]
    public void User_SerializesToRoleUser()
    {
        var json = JsonSerializer.Serialize(Role.User, A2AJsonUtilities.DefaultOptions);

        Assert.Equal("\"ROLE_USER\"", json);
    }

    [Fact]
    public void Agent_SerializesToRoleAgent()
    {
        var json = JsonSerializer.Serialize(Role.Agent, A2AJsonUtilities.DefaultOptions);

        Assert.Equal("\"ROLE_AGENT\"", json);
    }

    [Fact]
    public void Unspecified_SerializesToRoleUnspecified()
    {
        var json = JsonSerializer.Serialize(Role.Unspecified, A2AJsonUtilities.DefaultOptions);

        Assert.Equal("\"ROLE_UNSPECIFIED\"", json);
    }

    [Fact]
    public void Deserialize_FromString_ParsesCorrectly()
    {
        var user = JsonSerializer.Deserialize<Role>("\"ROLE_USER\"", A2AJsonUtilities.DefaultOptions);
        var agent = JsonSerializer.Deserialize<Role>("\"ROLE_AGENT\"", A2AJsonUtilities.DefaultOptions);
        var unspecified = JsonSerializer.Deserialize<Role>("\"ROLE_UNSPECIFIED\"", A2AJsonUtilities.DefaultOptions);

        Assert.Equal(Role.User, user);
        Assert.Equal(Role.Agent, agent);
        Assert.Equal(Role.Unspecified, unspecified);
    }

    [Theory]
    [InlineData("\"user\"", Role.User)]
    [InlineData("\"agent\"", Role.Agent)]
    [InlineData("\"unspecified\"", Role.Unspecified)]
    public void Deserialize_SpecCompliantNames_ParsesCorrectly(string json, Role expected)
    {
        var result = JsonSerializer.Deserialize<Role>(json, A2AJsonUtilities.DefaultOptions);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Deserialize_SpecCompliantMessage_ParsesRole()
    {
        const string json = """{"messageId":"123","role":"user","parts":[{"text":"hello"}]}""";
        var message = JsonSerializer.Deserialize<Message>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(message);
        Assert.Equal(Role.User, message.Role);
    }
}
