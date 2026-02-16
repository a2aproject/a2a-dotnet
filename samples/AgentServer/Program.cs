using A2A;
using A2A.AspNetCore;
using AgentServer;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("A2AAgentServer"))
    .WithTracing(tracing => tracing
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

// Create store and logger
var store = new InMemoryTaskStore();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger<TaskManager>();

switch (agentType.ToLowerInvariant())
{
    case "echo":
        var echoAgent = new EchoAgent();
        var echoTaskManager = new TaskManager(store, logger);
        echoAgent.Attach(echoTaskManager);
        app.MapA2A(echoTaskManager, "/echo");
        app.MapWellKnownAgentCard(echoAgent.GetAgentCard("http://localhost:5048/echo"), "/echo");
        break;

    case "echotasks":
        var echoTasksAgent = new EchoAgentWithTasks();
        var echoTasksManager = new TaskManager(store, logger);
        echoTasksAgent.Attach(echoTasksManager, store);
        app.MapA2A(echoTasksManager, "/echotasks");
        app.MapWellKnownAgentCard(echoTasksAgent.GetAgentCard("http://localhost:5048/echotasks"), "/echotasks");
        break;

    case "researcher":
        var researcherAgent = new ResearcherAgent();
        var researcherManager = new TaskManager(store, logger);
        researcherAgent.Attach(researcherManager, store);
        app.MapA2A(researcherManager, "/researcher");
        app.MapWellKnownAgentCard(researcherAgent.GetAgentCard("http://localhost:5048/researcher"), "/researcher");
        break;

    case "speccompliance":
        var specAgent = new SpecComplianceAgent();
        var specManager = new TaskManager(store, logger);
        specAgent.Attach(specManager, store);
        app.MapA2A(specManager, "/speccompliance");
        var specCard = specAgent.GetAgentCard("http://localhost:5048/speccompliance");
        app.MapWellKnownAgentCard(specCard);
        app.MapWellKnownAgentCard(specCard, "/speccompliance");
        break;

    default:
        Console.WriteLine($"Unknown agent type: {agentType}");
        Environment.Exit(1);
        return;
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