using System.Net;
using System.Text;
using System.Text.Json;

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

    [Fact]
    public async Task Create_HttpJsonClient_UsesUrlFromMatchingInterface()
    {
        HttpRequestMessage? captured = null;
        var card = CreateCard(ProtocolBindingNames.HttpJson, "http://my-rest-agent.example.com/a2a/v1");
        var httpClient = CreateCapturingHttpClient(
            CreateJsonResponse(new SendMessageResponse { Message = new Message { MessageId = "m-1", Role = Role.User, Parts = [] } }),
            req => captured = req);

        var client = A2AClientFactory.Create(card, httpClient);

        Assert.IsType<A2AHttpJsonClient>(client);
        await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("hi")], Role = Role.User, MessageId = "m-1" }
        });
        Assert.NotNull(captured);
        Assert.StartsWith("http://my-rest-agent.example.com/a2a/v1/", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task Create_JsonRpcClient_UsesUrlFromMatchingInterface()
    {
        HttpRequestMessage? captured = null;
        var card = CreateCard(ProtocolBindingNames.JsonRpc, "http://my-rpc-agent.example.com/a2a/v1");
        var rpcResponse = new JsonRpcResponse { Id = new JsonRpcId("1"), Result = JsonSerializer.SerializeToNode(
            new SendMessageResponse { Message = new Message { MessageId = "m-1", Role = Role.User, Parts = [] } },
            A2AJsonUtilities.DefaultOptions) };
        var httpClient = CreateCapturingHttpClient(CreateJsonResponse(rpcResponse), req => captured = req);

        var client = A2AClientFactory.Create(card, httpClient);

        Assert.IsType<A2AClient>(client);
        await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("hi")], Role = Role.User, MessageId = "m-1" }
        });
        Assert.NotNull(captured);
        Assert.Equal("http://my-rpc-agent.example.com/a2a/v1", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task Create_WithBothBindings_DefaultPreference_UsesHttpUrlNotRpcUrl()
    {
        HttpRequestMessage? captured = null;
        var card = new AgentCard
        {
            Name = "test",
            Description = "d",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface { ProtocolBinding = ProtocolBindingNames.HttpJson, Url = "http://rest-endpoint.example.com/rest" },
                new AgentInterface { ProtocolBinding = ProtocolBindingNames.JsonRpc, Url = "http://rpc-endpoint.example.com/rpc" }
            ]
        };
        var httpClient = CreateCapturingHttpClient(
            CreateJsonResponse(new SendMessageResponse { Message = new Message { MessageId = "m-1", Role = Role.User, Parts = [] } }),
            req => captured = req);

        var client = A2AClientFactory.Create(card, httpClient);

        Assert.IsType<A2AHttpJsonClient>(client);
        await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("hi")], Role = Role.User, MessageId = "m-1" }
        });
        Assert.NotNull(captured);
        Assert.StartsWith("http://rest-endpoint.example.com/rest/", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task Create_PreferJsonRpc_UsesRpcUrlNotRestUrl()
    {
        HttpRequestMessage? captured = null;
        var card = new AgentCard
        {
            Name = "test",
            Description = "d",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface { ProtocolBinding = ProtocolBindingNames.HttpJson, Url = "http://rest-endpoint.example.com/rest" },
                new AgentInterface { ProtocolBinding = ProtocolBindingNames.JsonRpc, Url = "http://rpc-endpoint.example.com/rpc" }
            ]
        };
        var options = new A2AClientOptions
        {
            PreferredBindings = [ProtocolBindingNames.JsonRpc, ProtocolBindingNames.HttpJson]
        };
        var rpcResponse = new JsonRpcResponse { Id = new JsonRpcId("1"), Result = JsonSerializer.SerializeToNode(
            new SendMessageResponse { Message = new Message { MessageId = "m-1", Role = Role.User, Parts = [] } },
            A2AJsonUtilities.DefaultOptions) };
        var httpClient = CreateCapturingHttpClient(CreateJsonResponse(rpcResponse), req => captured = req);

        var client = A2AClientFactory.Create(card, httpClient, options);

        Assert.IsType<A2AClient>(client);
        await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("hi")], Role = Role.User, MessageId = "m-1" }
        });
        Assert.NotNull(captured);
        Assert.Equal("http://rpc-endpoint.example.com/rpc", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task Create_CaseInsensitiveBinding_UsesCorrectUrl()
    {
        HttpRequestMessage? captured = null;
        var card = new AgentCard
        {
            Name = "test",
            Description = "d",
            Version = "1.0",
            SupportedInterfaces = [new AgentInterface { ProtocolBinding = "http+JSON", Url = "http://mixed-case.example.com/api" }]
        };
        var httpClient = CreateCapturingHttpClient(
            CreateJsonResponse(new SendMessageResponse { Message = new Message { MessageId = "m-1", Role = Role.User, Parts = [] } }),
            req => captured = req);

        var client = A2AClientFactory.Create(card, httpClient);

        Assert.IsType<A2AHttpJsonClient>(client);
        await client.SendMessageAsync(new SendMessageRequest
        {
            Message = new Message { Parts = [Part.FromText("hi")], Role = Role.User, MessageId = "m-1" }
        });
        Assert.NotNull(captured);
        Assert.StartsWith("http://mixed-case.example.com/api/", captured.RequestUri!.ToString());
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

    private static HttpResponseMessage CreateJsonResponse<T>(T body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, A2AJsonUtilities.DefaultOptions),
                Encoding.UTF8,
                "application/json")
        };

    private static HttpClient CreateCapturingHttpClient(HttpResponseMessage response, Action<HttpRequestMessage> capture) =>
        new(new MockHttpMessageHandler(response, capture));
}
