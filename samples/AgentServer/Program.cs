using A2A;
using A2A.AspNetCore;
using AgentServer;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("A2AAgentServer"))
    .WithTracing(tracing => tracing
        .AddSource(TaskManager.ActivitySource.Name)
        .AddSource(A2AJsonRpcProcessor.ActivitySource.Name)
        .AddSource(ResearcherAgent.ActivitySource.Name)
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

// Get the agent type from command line arguments
var agentType = GetAgentTypeFromArgs(args);

// Create and register the specified agent
var taskManager = new TaskManager();
var agentCardProvider = new AgentCardProvider();

switch (agentType.ToLowerInvariant())
{
    case "echo":
        var echoAgent = new EchoAgent();
        echoAgent.Attach(taskManager, agentCardProvider);
        app.MapA2A(taskManager, agentCardProvider, "/echo");
        app.MapWellKnownAgentCard(agentCardProvider, "/echo");
        app.MapHttpA2A(taskManager, agentCardProvider, "/echo");
        break;

    case "echotasks":
        var echoAgentWithTasks = new EchoAgentWithTasks();
        echoAgentWithTasks.Attach(taskManager, agentCardProvider);
        app.MapA2A(taskManager, agentCardProvider, "/echotasks");
        app.MapWellKnownAgentCard(agentCardProvider, "/echotasks");
        app.MapHttpA2A(taskManager, agentCardProvider, "/echotasks");
        break;

    case "researcher":
        var researcherAgent = new ResearcherAgent();
        researcherAgent.Attach(taskManager, agentCardProvider);
        app.MapA2A(taskManager, agentCardProvider, "/researcher");
        app.MapWellKnownAgentCard(agentCardProvider, "/researcher");
        break;

    case "speccompliance":
        var specComplianceAgent = new SpecComplianceAgent();
        specComplianceAgent.Attach(taskManager, agentCardProvider);
        app.MapA2A(taskManager, agentCardProvider, "/speccompliance");
        app.MapWellKnownAgentCard(agentCardProvider, "/speccompliance");
        break;

    default:
        Console.WriteLine($"Unknown agent type: {agentType}");
        Environment.Exit(1);
        return;
}

app.Run();

static string GetAgentTypeFromArgs(string[] args)
{
    // Look for --agent parameter
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--agent" || args[i] == "-a")
        {
            return args[i + 1];
        }
    }

    // Default to echo if no agent specified
    Console.WriteLine("No agent specified. Use --agent or -a parameter to specify agent type (echo, echotasks, researcher, speccompliance). Defaulting to 'echo'.");
    return "echo";
}