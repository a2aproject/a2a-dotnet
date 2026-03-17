namespace A2A.V0_3Compat.UnitTests;

public class A2AClientFactoryTests
{
    [Fact]
    public void CreateFromAgentCard_WithV10Card_ReturnsA2AClient()
    {
        var cardJson = """
        {
            "name": "Test Agent",
            "description": "A test agent",
            "version": "1.0",
            "supportedInterfaces": [{ "url": "http://localhost/a2a", "protocolBinding": "JSONRPC", "protocolVersion": "1.0" }],
            "capabilities": { "streaming": true },
            "skills": [],
            "defaultInputModes": ["text"],
            "defaultOutputModes": ["text"]
        }
        """;

        var client = VersionNegotiatingClientFactory.CreateFromAgentCard(cardJson, new Uri("http://localhost"));

        Assert.IsType<A2A.A2AClient>(client);
    }

    [Fact]
    public void CreateFromAgentCard_WithV03Card_ReturnsV03Adapter()
    {
        var cardJson = """
        {
            "name": "Legacy Agent",
            "description": "A legacy agent",
            "version": "1.0",
            "url": "http://localhost/a2a",
            "protocolVersion": "0.3.0",
            "capabilities": { "streaming": true },
            "skills": [],
            "defaultInputModes": ["text"],
            "defaultOutputModes": ["text"],
            "preferredTransport": "jsonrpc"
        }
        """;

        var client = VersionNegotiatingClientFactory.CreateFromAgentCard(cardJson, new Uri("http://localhost"));

        // V03ClientAdapter is internal, so check it's NOT A2AClient but is IA2AClient
        Assert.IsNotType<A2A.A2AClient>(client);
        Assert.IsAssignableFrom<A2A.IA2AClient>(client);
    }

    [Fact]
    public void CreateFromAgentCard_WithV03Card_FallbackDisabled_Throws()
    {
        var cardJson = """
        {
            "name": "Legacy Agent",
            "description": "A legacy agent",
            "version": "1.0",
            "url": "http://localhost/a2a",
            "protocolVersion": "0.3.0",
            "capabilities": {},
            "skills": [],
            "defaultInputModes": ["text"],
            "defaultOutputModes": ["text"],
            "preferredTransport": "jsonrpc"
        }
        """;

        var options = new VersionNegotiationOptions { AllowV03Fallback = false };

        Assert.Throws<InvalidOperationException>(() =>
            VersionNegotiatingClientFactory.CreateFromAgentCard(cardJson, new Uri("http://localhost"), options: options));
    }

    [Fact]
    public void CreateFromAgentCard_WithV10Card_UsesInterfaceUrl()
    {
        var cardJson = """
        {
            "name": "Test Agent",
            "description": "An agent with interface URL",
            "version": "1.0",
            "supportedInterfaces": [{ "url": "http://specific-host/a2a", "protocolBinding": "JSONRPC", "protocolVersion": "1.0" }],
            "capabilities": {},
            "skills": []
        }
        """;

        var client = VersionNegotiatingClientFactory.CreateFromAgentCard(cardJson, new Uri("http://fallback-host"));

        Assert.IsType<A2A.A2AClient>(client);
    }

    [Fact]
    public void CreateFromAgentCard_WithEmptySupportedInterfaces_FallsBackToV03()
    {
        var cardJson = """
        {
            "name": "Ambiguous Agent",
            "description": "An agent with empty supportedInterfaces",
            "version": "1.0",
            "supportedInterfaces": [],
            "url": "http://localhost/a2a",
            "capabilities": {},
            "skills": []
        }
        """;

        var client = VersionNegotiatingClientFactory.CreateFromAgentCard(cardJson, new Uri("http://localhost"));

        // Empty supportedInterfaces should fall back to v0.3
        Assert.IsNotType<A2A.A2AClient>(client);
        Assert.IsAssignableFrom<A2A.IA2AClient>(client);
    }
}
