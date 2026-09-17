using A2A;
using A2A.AspNetCore;
using A2A.Itk;
using A2A.V0_3Compat;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var httpPort = 10102;
var grpcPort = 11002;

// Parse CLI args (ITK passes --httpPort and --grpcPort)
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--httpPort" && i + 1 < args.Length)
        httpPort = int.Parse(args[++i]);
    else if (args[i] == "--grpcPort" && i + 1 < args.Length)
        grpcPort = int.Parse(args[++i]);
}

builder.WebHost.UseUrls($"http://127.0.0.1:{httpPort}");

var agentCard = ItkAgent.GetAgentCard(httpPort);

// Registered before AddA2AAgent, whose TryAddSingleton then leaves it alone. ActsServer
// only adds the extended agent card, which the stock server has no way to produce.
builder.Services.AddSingleton<IA2ARequestHandler>(sp => new ActsServer(
    sp.GetRequiredService<IAgentHandler>(),
    sp.GetRequiredService<ITaskStore>(),
    sp.GetRequiredService<ChannelEventNotifier>(),
    sp.GetRequiredService<ILogger<A2AServer>>(),
    sp.GetRequiredService<A2AServerOptions>(),
    ActsServer.Extend(agentCard)));

builder.Services.TryAddSingleton<ChannelEventNotifier>();
builder.Services.TryAddSingleton<ITaskStore, InMemoryTaskStore>();
builder.Services.AddA2AAgent<ItkAgent>(agentCard);
builder.Services.AddHttpClient();

var app = builder.Build();

// Ahead of every endpoint: the ACTS SEC-* tests need the credential checked before the
// request reaches a handler, whichever binding carries it.
app.UseActsCredentials();

// Serve the agent card at /.well-known/agent-card.json, in whichever dialect the
// caller asked for. A v0.3 peer sends no A2A-Version header and cannot read a v1.0
// card, so it gets the v0.3 shape; ACTS and the v1.0 peers send `A2A-Version: 1.0`
// and get the v1.0 one. The default is blended, so a reader that sends no header
// still finds `supportedInterfaces` — which is what the ITK readiness probe does.
app.MapAgentCardGetWithV03Compat(() => Task.FromResult(agentCard));

// Also serve at /jsonrpc/.well-known/agent-card.json (ITK readiness check path)
app.MapGet("/jsonrpc/.well-known/agent-card.json", () => Results.Ok(agentCard));

// JSON-RPC at /jsonrpc (ITK expects this path) and at the root, both serving v0.3
// and v1.0 from one endpoint. V03ServerProcessor routes on the A2A-Version header
// and passes a 1.0 request through untouched, so this is a superset of MapA2A.
//
// One endpoint rather than a separate v0.3 path because the v0.3 card cannot
// express two: ToV03AgentCard takes its single top-level `url` from the first
// JSONRPC interface on the v1.0 card, so whatever that entry points at is where
// every v0.3 peer will dial.
var requestHandler = app.Services.GetRequiredService<IA2ARequestHandler>();
app.MapA2AWithV03Compat(requestHandler, "/jsonrpc");
app.MapA2AWithV03Compat(requestHandler, "/");

// HTTP+JSON REST endpoints at root
app.MapHttpA2A(requestHandler);

app.Run();
