using A2A;
using A2A.AspNetCore;
using A2A.Itk;

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
builder.Services.AddA2AAgent<ItkAgent>(agentCard);
builder.Services.AddHttpClient();

var app = builder.Build();

// Serve the agent card at /.well-known/agent-card.json
app.MapWellKnownAgentCard(agentCard);

// Also serve at /jsonrpc/.well-known/agent-card.json (ITK readiness check path)
app.MapGet("/jsonrpc/.well-known/agent-card.json", () => Results.Ok(agentCard));

// JSON-RPC endpoint at /jsonrpc (ITK expects this path)
app.MapA2A("/jsonrpc");

// Also map at root for direct access
app.MapA2A("/");

// HTTP+JSON REST endpoints at root
var handler = app.Services.GetRequiredService<IA2ARequestHandler>();
app.MapHttpA2A(handler);

app.Run();
