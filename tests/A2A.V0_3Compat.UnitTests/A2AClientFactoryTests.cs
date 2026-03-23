namespace A2A.V0_3Compat.UnitTests;

public class A2AClientFactoryTests : IDisposable
{
    public void Dispose()
    {
        A2AClientFactory.ClearFallback();
        GC.SuppressFinalize(this);
    }

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
    public void Create_WithV03Card_AndGlobalFallback_ReturnsV03Adapter()
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
    public void Create_WithV03Card_AndPerCallFallback_ReturnsV03Adapter()
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

        var options = new A2AClientOptions
        {
            FallbackFactory = V03FallbackRegistration.CreateV03Client,
        };

        var client = A2AClientFactory.Create(cardJson, new Uri("http://localhost"), options: options);

        Assert.IsNotType<A2A.A2AClient>(client);
        Assert.IsAssignableFrom<A2A.IA2AClient>(client);
    }

    [Fact]
    public void Create_WithV03Card_NoFallback_Throws()
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
    public void Create_WithEmptySupportedInterfaces_UsesPerCallFallback()
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

        var options = new A2AClientOptions
        {
            FallbackFactory = V03FallbackRegistration.CreateV03Client,
        };

        var client = A2AClientFactory.Create(cardJson, new Uri("http://localhost"), options: options);

        Assert.IsNotType<A2A.A2AClient>(client);
        Assert.IsAssignableFrom<A2A.IA2AClient>(client);
    }
}
