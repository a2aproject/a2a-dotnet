using A2A;
using A2A.AspNetCore;
using A2A.Grpc.AspNetCore;
using ExtensionNegotiation;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;
using System.Text.Json;

var builder = WebApplication.CreateSlimBuilder();
builder.Configuration.Sources.Clear();
builder.Configuration.AddCommandLine(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var httpPort = builder.Configuration.GetValue("http-port", 5080);
var grpcPort = builder.Configuration.GetValue("grpc-port", 5081);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(httpPort);
ArgumentOutOfRangeException.ThrowIfGreaterThan(httpPort, 65535);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grpcPort);
ArgumentOutOfRangeException.ThrowIfGreaterThan(grpcPort, 65535);
if (httpPort == grpcPort) throw new ArgumentException("HTTP and gRPC ports must be different.");
var httpAddress = new Uri($"http://127.0.0.1:{httpPort}");
var grpcAddress = new Uri($"http://127.0.0.1:{grpcPort}");
builder.WebHost.PreferHostingUrls(false);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, httpPort, endpoint => endpoint.Protocols = HttpProtocols.Http1);
    options.Listen(IPAddress.Loopback, grpcPort, endpoint => endpoint.Protocols = HttpProtocols.Http2);
});

// 1. Support and ownership are registered once, independently of any request.
var availability = new DemoAvailability();
var operations = DebateOperations.Create(availability);
var registry = operations.Registry;

// 2. Each binding asks the same availability policy once per requested extension.
var httpExtensions = new A2AExtensionOptions(registry)
{
    CanActivate = (_, _, ct) => availability.CanActivate(ct),
};
builder.Services.AddA2AAgent<EchoAgent>(httpExtensions, new AgentCard
{
    Name = "Debate extension demo",
    Description = "Resumes a demo debate and streams a product draft",
    Version = "1.0.0",
    DefaultInputModes = ["text/plain"],
    DefaultOutputModes = ["text/plain"],
    Capabilities = new AgentCapabilities { Streaming = true },
    SupportedInterfaces =
    [
        new AgentInterface { Url = new Uri(httpAddress, "/rpc").AbsoluteUri, ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" },
        new AgentInterface { Url = new Uri(httpAddress, "/http").AbsoluteUri, ProtocolBinding = "HTTP+JSON", ProtocolVersion = "1.0" },
        new AgentInterface { Url = $"127.0.0.1:{grpcPort}", ProtocolBinding = "GRPC", ProtocolVersion = "1.0" },
    ],
    Skills = [new AgentSkill { Id = "echo", Name = "Echo", Description = "Replies without an extension", Tags = ["demo"] }],
});
builder.Services.AddA2AGrpc(new A2AGrpcExtensionOptions(registry)
{
    CanActivate = (_, _, ct) => availability.CanActivate(ct),
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Add(A2AJsonUtilities.DefaultOptions.TypeInfoResolver!));

// 3. Bind the same handles to transport-specific names and routes.
var rpcBindings = new A2AJsonRpcCustomOperationBuilder()
    .Map("debate/resume", operations.Resume)
    .MapStreaming("debate/watch", operations.Watch)
    .Build(registry);
var restBindings = new A2AHttpCustomOperationBuilder()
    .Map(HttpMethods.Post, "/debate:resume", operations.Resume, ReadRequestAsync)
    .MapStreaming(HttpMethods.Post, "/debate:watch", operations.Watch, ReadRequestAsync)
    .Build(registry);
await using var app = builder.Build();
var handler = app.Services.GetRequiredService<IA2ARequestHandler>();
app.MapA2A(handler, "/rpc", httpExtensions, rpcBindings);
app.MapHttpA2A(handler, "/http", httpExtensions, restBindings);
app.MapGrpcA2A();
app.MapGrpcA2ACustomOperations();
app.MapWellKnownAgentCard(registry, app.Services.GetRequiredService<AgentCard>());

// 4. Exercise real loopback connections and stop; no separate server or model key is needed.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await app.StartAsync(timeout.Token);
try
{
    await new NegotiationDemo(httpAddress, grpcAddress, operations, availability).RunAsync(timeout.Token);
}
finally
{
    await app.StopAsync(CancellationToken.None);
}

static async ValueTask<ResumeDebateRequest> ReadRequestAsync(HttpContext context, CancellationToken cancellationToken)
    => await JsonSerializer.DeserializeAsync(context.Request.Body, DebateJsonContext.Default.ResumeDebateRequest, cancellationToken)
        ?? throw new A2AException("A debate request is required.", A2AErrorCode.InvalidParams);

internal sealed class EchoAgent : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        => await new MessageResponder(eventQueue, context.ContextId).ReplyAsync(
            context.ExtensionActivation?.ActivatedExtensions.Contains(DebateOperations.ExtensionUri) == true
                ? "Debate extension active: " + context.UserText
                : context.UserText ?? "No text provided.",
            cancellationToken: cancellationToken);
}
