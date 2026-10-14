using A2A;
using A2A.Grpc;
using A2A.Grpc.Extensions.Protos;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;

namespace ExtensionNegotiation;

internal sealed class NegotiationDemo(
    Uri httpAddress, Uri grpcAddress, DebateOperations operations, DemoAvailability availability)
{
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        using var plainHttp = CreateHttpClient();
        using var discovery = await plainHttp.GetAsync(new Uri(httpAddress, "/.well-known/agent-card.json"), cancellationToken);
        discovery.EnsureSuccessStatusCode();
        var card = await new A2ACardResolver(httpAddress, httpClient: plainHttp).GetAgentCardAsync(cancellationToken: cancellationToken);
        Ensure(card.Capabilities.Extensions?.Single().Uri == DebateOperations.ExtensionUri, "Card support must come from the registry.");
        Ensure(!discovery.Headers.Contains("A2A-Extensions"), "Discovery must not imply request activation.");
        Console.WriteLine("Discovery: extension supported, no request activation.");

        try
        {
            await operations.Registry.InvokeAsync(operations.Resume, new A2ACustomOperationContext(),
                new ResumeDebateRequest("debate-42"), cancellationToken);
            throw new InvalidOperationException("Direct invocation bypassed extension negotiation.");
        }
        catch (A2AException error) when (error.ErrorCode == A2AErrorCode.ExtensionSupportRequired)
        {
            Ensure(availability.Invocations == 0, "The direct invocation guard must run before the handler.");
            Console.WriteLine("Direct invocation: missing negotiated context rejected.");
        }

        foreach (var jsonRpc in new[] { true, false })
        {
            var binding = jsonRpc ? "JSON-RPC" : "HTTP+JSON";
            using var requestedHttp = CreateHttpClient(DebateOperations.ExtensionUri);
            using var wrongVersionHttp = CreateHttpClient("https://example.org/extensions/debate/v2");
            availability.IsAvailable = true;
            await VerifyHttpUnaryAsync(plainHttp, jsonRpc, accepted: false, cancellationToken);
            await VerifyHttpUnaryAsync(wrongVersionHttp, jsonRpc, accepted: false, cancellationToken);
            await VerifyHttpUnaryAsync(requestedHttp, jsonRpc, accepted: true, cancellationToken);
            await VerifyHttpStreamAsync(requestedHttp, jsonRpc, cancellationToken);
            // Watch changed availability. The next request must negotiate again and decline.
            await VerifyHttpUnaryAsync(requestedHttp, jsonRpc, accepted: false, cancellationToken);
            availability.IsAvailable = true;
            if (jsonRpc)
            {
                using var client = new A2AClient(new Uri(httpAddress, "/rpc"), requestedHttp);
                await VerifySdkErrorAsync(client, binding, cancellationToken);
            }
            else
            {
                using var client = new A2AHttpJsonClient(new Uri(httpAddress, "/http"), requestedHttp);
                await VerifySdkErrorAsync(client, binding, cancellationToken);
            }
            Console.WriteLine($"{binding}: missing and unsupported requests rejected; activation, stream snapshot, later decline and SDK error passed.");
        }

        await VerifyGrpcAsync(cancellationToken);
        Ensure(availability.Invocations == 3, "Only one accepted unary call per binding may reach the handler.");
        Console.WriteLine("Extension negotiation demo passed.");
    }

    private async Task VerifyHttpUnaryAsync(HttpClient http, bool jsonRpc, bool accepted, CancellationToken cancellationToken)
    {
        var before = availability.Invocations;
        using var request = CreateHttpRequest(jsonRpc, streaming: false);
        using var response = await http.SendAsync(request, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (accepted)
        {
            response.EnsureSuccessStatusCode();
            Ensure(ReadActivation(response), "Accepted HTTP operation must report activation.");
            var payload = jsonRpc ? document.RootElement.GetProperty("result") : document.RootElement;
            var result = payload.Deserialize(DebateJsonContext.Default.ResumeDebateResult)!;
            Ensure(result.IsActive && result.DebateId == "debate-42", "The handler must see the negotiated snapshot.");
            Ensure(availability.Invocations == before + 1, "The accepted handler must execute once.");
        }
        else
        {
            Ensure(!response.Headers.Contains("A2A-Extensions"), "A rejected optional extension must not be reported as active.");
            var error = document.RootElement.GetProperty("error");
            if (jsonRpc) Ensure(error.GetProperty("code").GetInt32() == (int)A2AErrorCode.ExtensionSupportRequired, "Expected the original JSON-RPC error code.");
            else Ensure(response.StatusCode == HttpStatusCode.BadRequest &&
                error.GetProperty("details")[0].GetProperty("reason").GetString() == "EXTENSION_SUPPORT_REQUIRED", "Expected the HTTP+JSON extension error.");
            Ensure(availability.Invocations == before, "Negotiation must reject before running the handler.");
        }
    }

    private async Task VerifyHttpStreamAsync(HttpClient http, bool jsonRpc, CancellationToken cancellationToken)
    {
        var before = availability.PolicyCalls;
        using var request = CreateHttpRequest(jsonRpc, streaming: true);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Ensure(ReadActivation(response), "Streaming response must report its initial activation.");
        using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var chunks = new List<DraftChunk>();
        await foreach (var item in SseParser.Create(body).EnumerateAsync(cancellationToken))
        {
            using var document = JsonDocument.Parse(item.Data);
            var payload = jsonRpc ? document.RootElement.GetProperty("result") : document.RootElement;
            chunks.Add(payload.Deserialize(DebateJsonContext.Default.DraftChunk)!);
        }
        VerifyStream(chunks, before);
    }

    private async Task VerifyGrpcAsync(CancellationToken cancellationToken)
    {
        using var transport = CreateHttpClient();
        using var channel = GrpcChannel.ForAddress(grpcAddress, new GrpcChannelOptions { HttpClient = transport });
        var client = new A2AExtensionService.A2AExtensionServiceClient(channel);
        var activeHeaders = new Metadata { { "a2a-version", "1.0" }, { "a2a-extensions", DebateOperations.ExtensionUri } };
        availability.IsAvailable = true;
        await VerifyGrpcRejectionAsync(new Metadata { { "a2a-version", "1.0" } });
        using (var call = client.InvokeExtensionOperationAsync(GrpcRequest(DebateOperations.ResumeId), activeHeaders, cancellationToken: cancellationToken))
        {
            var response = await call.ResponseAsync;
            var result = JsonSerializer.Deserialize(response.Payload.Span, DebateJsonContext.Default.ResumeDebateResult)!;
            Ensure(result.IsActive && result.DebateId == "debate-42", "gRPC handler must see the negotiated snapshot.");
            Ensure((await call.ResponseHeadersAsync).GetValue("a2a-extensions") == DebateOperations.ExtensionUri, "gRPC initial metadata must report activation.");
        }
        var before = availability.PolicyCalls;
        var chunks = new List<DraftChunk>();
        using (var call = client.InvokeStreamingExtensionOperation(GrpcRequest(DebateOperations.WatchId), activeHeaders, cancellationToken: cancellationToken))
        {
            await foreach (var item in call.ResponseStream.ReadAllAsync(cancellationToken))
                chunks.Add(JsonSerializer.Deserialize(item.Payload.Span, DebateJsonContext.Default.DraftChunk)!);
            Ensure((await call.ResponseHeadersAsync).GetValue("a2a-extensions") == DebateOperations.ExtensionUri, "gRPC stream must retain its initial metadata.");
        }
        VerifyStream(chunks, before);
        await VerifyGrpcRejectionAsync(activeHeaders);
        availability.IsAvailable = true;
        using var requestedTransport = CreateHttpClient(DebateOperations.ExtensionUri);
        using var sdkClient = new A2AGrpcClient(grpcAddress, requestedTransport);
        await VerifySdkErrorAsync(sdkClient, "gRPC", cancellationToken);
        Console.WriteLine("gRPC: missing request rejected; activation, stream snapshot, later decline and SDK error passed.");

        async Task VerifyGrpcRejectionAsync(Metadata headers)
        {
            var previousInvocations = availability.Invocations;
            using var call = client.InvokeExtensionOperationAsync(GrpcRequest(DebateOperations.ResumeId), headers, cancellationToken: cancellationToken);
            try { await call.ResponseAsync; }
            catch (RpcException error)
            {
                Ensure(error.StatusCode == StatusCode.FailedPrecondition, "Expected the gRPC extension error.");
                Ensure((await call.ResponseHeadersAsync).Get("a2a-extensions") is null, "Rejected extension must have no initial activation metadata.");
                Ensure(error.Trailers.Get("a2a-extensions") is null, "Rejected extension must have no activated trailer.");
                Ensure(availability.Invocations == previousInvocations, "Rejected gRPC calls must not run the handler.");
                return;
            }
            throw new InvalidOperationException("Expected gRPC extension activation to be rejected.");
        }
    }

    private void VerifyStream(List<DraftChunk> chunks, int previousPolicyCalls)
    {
        Ensure(chunks.Count == 2 && chunks[0].Sequence == 1 && chunks[1].Sequence == 2, "Expected two ordered draft chunks.");
        Ensure(chunks.All(chunk => chunk.IsActive), "The active set must stay fixed for the entire stream.");
        Ensure(chunks[0].AvailableForNewRequests && !chunks[1].AvailableForNewRequests, "Availability must change independently of the active snapshot.");
        Ensure(availability.PolicyCalls == previousPolicyCalls + 1, "Availability must be evaluated only once for the stream.");
    }

    private static async Task VerifySdkErrorAsync(IA2AClient client, string binding, CancellationToken cancellationToken)
    {
        try { await client.GetTaskAsync(new GetTaskRequest { Id = "missing-demo-task" }, cancellationToken); }
        catch (A2AException error)
        {
            Ensure(error.ErrorCode == A2AErrorCode.TaskNotFound, "The original operation error must be preserved.");
            Ensure(error.ActivatedExtensions.SetEquals([DebateOperations.ExtensionUri]), "SDK errors must carry the server's active snapshot.");
            Console.WriteLine($"{binding} SDK error: {error.ErrorCode}, debate extension still active.");
            return;
        }
        throw new InvalidOperationException("Expected a missing-task error.");
    }

    private HttpRequestMessage CreateHttpRequest(bool jsonRpc, bool streaming)
    {
        var method = streaming ? "debate/watch" : "debate/resume";
        var path = jsonRpc ? "/rpc" : streaming ? "/http/debate:watch" : "/http/debate:resume";
        var payload = JsonSerializer.Serialize(new ResumeDebateRequest("debate-42"), DebateJsonContext.Default.ResumeDebateRequest);
        var body = jsonRpc ? $$"""{"jsonrpc":"2.0","id":"demo","method":"{{method}}","params":{{payload}}}""" : payload;
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(httpAddress, path))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("A2A-Version", "1.0");
        if (streaming) request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    private static ExtensionOperationRequest GrpcRequest(string id) => new()
    {
        OperationId = id,
        Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(
            new ResumeDebateRequest("debate-42"), DebateJsonContext.Default.ResumeDebateRequest)),
    };

    private static bool ReadActivation(HttpResponseMessage response)
        => response.Headers.TryGetValues("A2A-Extensions", out var values) &&
            values.Single() == DebateOperations.ExtensionUri;

    private static HttpClient CreateHttpClient(string? requestedExtension = null)
        => new(new RequestHeadersHandler(requestedExtension)
        {
            InnerHandler = new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false },
        })
        { Timeout = TimeSpan.FromSeconds(15) };

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Immutable per-client configuration is enough for this single-agent demo. For concurrent
    // callers choosing different extensions, select metadata from each request's own state.
    private sealed class RequestHeadersHandler(string? requestedExtension) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (requestedExtension is not null) request.Headers.Add("A2A-Extensions", requestedExtension);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
