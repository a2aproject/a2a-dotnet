namespace A2A.V0_3Compat.UnitTests;

public class A2AClientFactoryTests
{
    [Fact]
    public void Create_WithV10Card_ReturnsA2AClient()
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

        var client = A2AClientFactory.Create(cardJson, new Uri("http://localhost"));

        Assert.IsType<A2A.A2AClient>(client);
    }

    [Fact]
    public void Create_WithV03Card_AndRegisteredBinding_ReturnsV03Adapter()
    {
        V03FallbackRegistration.Register();

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

        var client = A2AClientFactory.Create(cardJson, new Uri("http://localhost"));

        Assert.IsNotType<A2A.A2AClient>(client);
        Assert.IsAssignableFrom<A2A.IA2AClient>(client);
    }

    [Fact]
    public void Create_WithV03Card_NoRegisteredBinding_Throws()
    {
        // Ensure no v0.3 binding is registered
        A2AClientFactory.Unregister("JSONRPC", "0.3");

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

        Assert.Throws<A2AException>(() =>
            A2AClientFactory.Create(cardJson, new Uri("http://localhost")));
    }

    [Fact]
    public void Create_WithV10Card_UsesInterfaceUrl()
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

        var client = A2AClientFactory.Create(cardJson, new Uri("http://fallback-host"));

        Assert.IsType<A2A.A2AClient>(client);
    }

    [Fact]
    public void Create_WithEmptySupportedInterfaces_AndRegisteredV03_ReturnsAdapter()
    {
        V03FallbackRegistration.Register();

        var cardJson = """
        {
            "name": "Ambiguous Agent",
            "description": "An agent with empty supportedInterfaces",
            "version": "1.0",
            "supportedInterfaces": [],
            "url": "http://localhost/a2a",
            "protocolVersion": "0.3",
            "capabilities": {},
            "skills": []
        }
        """;

        var client = A2AClientFactory.Create(cardJson, new Uri("http://localhost"));

        Assert.IsNotType<A2A.A2AClient>(client);
        Assert.IsAssignableFrom<A2A.IA2AClient>(client);
    }

    [Fact]
    public void Create_V03Card_NormalizesVersionWithPatch()
    {
        V03FallbackRegistration.Register();

        // protocolVersion "0.3.0" should normalize to "0.3" and match
        var cardJson = """
        {
            "name": "Legacy Agent",
            "description": "A legacy agent",
            "url": "http://localhost/a2a",
            "protocolVersion": "0.3.0",
            "capabilities": {},
            "skills": []
        }
        """;

        var client = A2AClientFactory.Create(cardJson, new Uri("http://localhost"));

        Assert.IsAssignableFrom<A2A.IA2AClient>(client);
    }
}
