namespace A2A.UnitTests.Client;

public class A2AClientFactoryTests
{
    [Fact]
    public void Create_DefaultPreferences_PrefersHttp()
    {
        var card = CreateCardWithBothBindings();

        var client = A2AClientFactory.Create(card);

        Assert.IsType<A2AHttpJsonClient>(client);
    }

    [Fact]
    public void Create_HttpOnlyCard_ReturnsHttpClient()
    {
        var card = CreateCard(ProtocolBindingNames.HttpJson, "http://agent/http");

        var client = A2AClientFactory.Create(card);

        Assert.IsType<A2AHttpJsonClient>(client);
    }

    [Fact]
    public void Create_JsonRpcOnlyCard_ReturnsJsonRpcClient()
    {
        var card = CreateCard(ProtocolBindingNames.JsonRpc, "http://agent/jsonrpc");

        var client = A2AClientFactory.Create(card);

        Assert.IsType<A2AClient>(client);
    }

    [Fact]
    public void Create_PreferJsonRpc_ReturnsJsonRpcClientEvenIfHttpAvailable()
    {
        var card = CreateCardWithBothBindings();
        var options = new A2AClientOptions
        {
            PreferredBindings = [ProtocolBindingNames.JsonRpc, ProtocolBindingNames.HttpJson]
        };

        var client = A2AClientFactory.Create(card, options: options);

        Assert.IsType<A2AClient>(client);
    }

    [Fact]
    public void Create_PreferJsonRpcOnly_FallsBackWhenNotAvailable()
    {
        var card = CreateCard(ProtocolBindingNames.HttpJson, "http://agent/http");
        var options = new A2AClientOptions
        {
            PreferredBindings = [ProtocolBindingNames.JsonRpc, ProtocolBindingNames.HttpJson]
        };

        var client = A2AClientFactory.Create(card, options: options);

        Assert.IsType<A2AHttpJsonClient>(client);
    }

    [Fact]
    public void Create_NoMatchingBinding_ThrowsA2AException()
    {
        var card = CreateCard(ProtocolBindingNames.HttpJson, "http://agent/http");
        var options = new A2AClientOptions
        {
            PreferredBindings = [ProtocolBindingNames.JsonRpc]
        };

        var ex = Assert.Throws<A2AException>(() => A2AClientFactory.Create(card, options: options));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("JSONRPC", ex.Message);
        Assert.Contains("HTTP", ex.Message);
    }

    [Fact]
    public void Create_EmptySupportedInterfaces_ThrowsA2AException()
    {
        var card = new AgentCard { Name = "test", Description = "d", Version = "1.0" };

        var ex = Assert.Throws<A2AException>(() => A2AClientFactory.Create(card));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("none", ex.Message);
    }

    [Fact]
    public void Create_NullAgentCard_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => A2AClientFactory.Create(null!));
    }

    [Fact]
    public void Create_BindingMatchIsCaseInsensitive()
    {
        var card = new AgentCard
        {
            Name = "test",
            Description = "d",
            Version = "1.0",
            SupportedInterfaces = [new AgentInterface { ProtocolBinding = "http+json", Url = "http://agent/http" }]
        };

        var client = A2AClientFactory.Create(card);

        Assert.IsType<A2AHttpJsonClient>(client);
    }

    [Fact]
    public void Create_PassesHttpClientThrough()
    {
        var card = CreateCard(ProtocolBindingNames.JsonRpc, "http://agent/jsonrpc");
        var httpClient = new HttpClient();

        var client = A2AClientFactory.Create(card, httpClient);

        Assert.IsType<A2AClient>(client);
    }

    // ---- Helpers ----

    private static AgentCard CreateCard(string binding, string url) => new()
    {
        Name = "test",
        Description = "d",
        Version = "1.0",
        SupportedInterfaces = [new AgentInterface { ProtocolBinding = binding, Url = url }]
    };

    private static AgentCard CreateCardWithBothBindings() => new()
    {
        Name = "test",
        Description = "d",
        Version = "1.0",
        SupportedInterfaces =
        [
            new AgentInterface { ProtocolBinding = ProtocolBindingNames.HttpJson, Url = "http://agent/http" },
            new AgentInterface { ProtocolBinding = ProtocolBindingNames.JsonRpc, Url = "http://agent/jsonrpc" }
        ]
    };
}
