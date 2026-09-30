using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2A.UnitTests.Client;

public partial class A2AClientCustomOperationTests
{
    [Fact]
    public async Task InvokeAsync_JsonRpcUsesRegisteredMethodAndSerializationMetadata()
    {
        var operation = new A2AOperation<CustomRequest, CustomResult>(
            new A2AOperationId("test.execute"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                operation,
                "test/execute",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomResult,
                static (message, request) =>
                    message.Headers.TryAddWithoutValidation(
                        "X-Custom-Value",
                        request.Value))
            .Build();
        string? capturedBody = null;
        string[]? capturedCustomHeader = null;
        var httpClient = new HttpClient(new RecordingHandler(
            request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync()
                    .GetAwaiter()
                    .GetResult();
                capturedCustomHeader = request.Headers
                    .GetValues("X-Custom-Value")
                    .ToArray();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "jsonrpc": "2.0",
                          "id": "response-1",
                          "result": { "value": "json-rpc-result" }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"),
                };
            }));
        var client = new A2AClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var result = await client.InvokeAsync(
            operation,
            new CustomRequest("request-value"));

        Assert.Equal("json-rpc-result", result.Value);
        using var requestJson = JsonDocument.Parse(capturedBody!);
        Assert.Equal(
            "test/execute",
            requestJson.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "request-value",
            requestJson.RootElement
                .GetProperty("params")
                .GetProperty("value")
                .GetString());
        Assert.Equal(
            "request-value",
            capturedCustomHeader!.Single());
    }

    [Fact]
    public async Task InvokeAsync_HttpJsonUsesRegisteredRequestMapperAndSerializationMetadata()
    {
        var operation = new A2AOperation<CustomRequest, CustomResult>(
            new A2AOperationId("test.execute"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapHttp(
                operation,
                static (endpoint, request, _) =>
                {
                    var message = new HttpRequestMessage(
                        HttpMethod.Post,
                        new Uri(endpoint, $"/tasks/{request.Value}:execute"))
                    {
                        Content = new StringContent(
                            """{"token":"opaque-token"}""",
                            Encoding.UTF8,
                            "application/json"),
                    };
                    message.Headers.TryAddWithoutValidation(
                        "X-Extension",
                        "enabled");
                    return ValueTask.FromResult(message);
                },
                CustomClientJsonContext.Default.CustomResult)
            .Build();
        HttpRequestMessage? capturedRequest = null;
        var httpClient = new HttpClient(new RecordingHandler(
            request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"value":"http-result"}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }));
        var client = new A2AHttpJsonClient(
            new Uri("http://localhost/a2a"),
            bindings,
            httpClient);

        var result = await client.InvokeAsync(
            operation,
            new CustomRequest("task-1"));

        Assert.Equal("http-result", result.Value);
        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "http://localhost/tasks/task-1:execute",
            capturedRequest.RequestUri!.ToString());
        Assert.Equal(
            "enabled",
            capturedRequest.Headers.GetValues("X-Extension").Single());
        Assert.Equal(
            "1.0",
            capturedRequest.Headers.GetValues("A2A-Version").Single());
    }

    [Fact]
    public async Task ClientFactory_CreateWithBindingsSupportsGenericInvocation()
    {
        var operation = new A2AOperation<CustomRequest, CustomResult>(
            new A2AOperationId("test.execute"));
        var bindings = new A2AClientOperationBindingBuilder()
            .MapJsonRpc(
                operation,
                "test/execute",
                CustomClientJsonContext.Default.CustomRequest,
                CustomClientJsonContext.Default.CustomResult)
            .Build();
        var httpClient = new HttpClient(new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "jsonrpc": "2.0",
                      "id": "response-1",
                      "result": { "value": "factory-result" }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));
        var agentCard = new AgentCard
        {
            Name = "test",
            Description = "test",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    ProtocolBinding = ProtocolBindingNames.JsonRpc,
                    Url = "http://localhost/a2a",
                },
            ],
        };

        var client = A2AClientFactory.Create(
            agentCard,
            bindings,
            httpClient);
        var result = await client.InvokeAsync(
            operation,
            new CustomRequest("request-value"));

        Assert.Equal("factory-result", result.Value);
    }

    [Fact]
    public void ClientFactory_CustomProtocolReceivesOperationBindings()
    {
        var protocolBinding = $"CUSTOM-{Guid.NewGuid():N}";
        var bindings = new A2AClientOperationBindingBuilder().Build();
        A2AClientOperationBindings? receivedBindings = null;
        A2AClientFactory.RegisterWithOperationBindings(
            protocolBinding,
            (_, _, operationBindings) =>
            {
                receivedBindings = operationBindings;
                throw new ExpectedFactoryInvocationException();
            });
        var agentCard = new AgentCard
        {
            Name = "test",
            Description = "test",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    ProtocolBinding = protocolBinding,
                    Url = "http://localhost/a2a",
                },
            ],
        };
        var options = new A2AClientOptions
        {
            PreferredBindings = [protocolBinding],
        };

        Assert.Throws<ExpectedFactoryInvocationException>(
            () => A2AClientFactory.Create(agentCard, bindings, options: options));
        Assert.Same(bindings, receivedBindings);
    }

    internal sealed record CustomRequest(
        [property: JsonPropertyName("value")] string Value);

    internal sealed record CustomResult(
        [property: JsonPropertyName("value")] string Value);

    [JsonSerializable(typeof(CustomRequest))]
    [JsonSerializable(typeof(CustomResult))]
    internal sealed partial class CustomClientJsonContext : JsonSerializerContext;

    private sealed class ExpectedFactoryInvocationException : Exception;

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
