using A2A;
using A2A.AspNetCore;
using AgentServer;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Get the agent type from command line arguments
var agentType = GetAgentTypeFromArgs(args);
var baseUrl = "http://localhost:5048";

// Register the appropriate agent via DI
switch (agentType.ToLowerInvariant())
{
    case "echo":
        builder.Services.AddA2AAgent<EchoAgent>(EchoAgent.GetAgentCard($"{baseUrl}/echo"));
        break;

    case "echotasks":
        builder.Services.AddA2AAgent<EchoAgentWithTasks>(EchoAgentWithTasks.GetAgentCard($"{baseUrl}/echotasks"));
        break;

    case "researcher":
        builder.Services.AddA2AAgent<ResearcherAgent>(ResearcherAgent.GetAgentCard($"{baseUrl}/researcher"));
        break;

    case "speccompliance":
        builder.Services.AddA2AAgent<SpecComplianceAgent>(SpecComplianceAgent.GetAgentCard($"{baseUrl}/speccompliance"));
        break;

    default:
        Console.WriteLine($"Unknown agent type: {agentType}");
        Environment.Exit(1);
        return;
}

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("A2AAgentServer"))
    .WithTracing(tracing => tracing
        .AddSource("A2A")
        .AddSource("A2A.AspNetCore")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
            options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        })
    );

var app = builder.Build();

app.UseHttpsRedirection();

// Add health endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow }));

// Map A2A endpoints using DI-registered services
var path = agentType.ToLowerInvariant() switch
{
    "echo" => "/echo",
    "echotasks" => "/echotasks",
    "researcher" => "/researcher",
    "speccompliance" => "/speccompliance",
    _ => "/agent",
};

app.MapA2A(path);

// For spec compliance, also map at root for well-known agent card discovery
if (agentType.Equals("speccompliance", StringComparison.OrdinalIgnoreCase))
{
    var card = app.Services.GetRequiredService<AgentCard>();
    app.MapWellKnownAgentCard(card);
}

app.Run();

static string GetAgentTypeFromArgs(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--agent" || args[i] == "-a")
        {
            return args[i + 1];
        }
    }

    Console.WriteLine("No agent specified. Use --agent or -a parameter to specify agent type (echo, echotasks, researcher, speccompliance). Defaulting to 'echo'.");
    return "echo";
}