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
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Get the agent type from command line arguments
var agentType = GetAgentTypeFromArgs(args);

// Create and register the specified agent
var taskManager = new TaskManager();

switch (agentType.ToLowerInvariant())
{
    case "echo":
        var echoAgent = new EchoAgent();
        echoAgent.Attach(taskManager);
        app.MapA2A(taskManager, "/echo");
        app.MapHttpA2A(taskManager, "/echo");
        break;

    case "echotasks":
        var echoAgentWithTasks = new EchoAgentWithTasks();
        echoAgentWithTasks.Attach(taskManager);
        app.MapA2A(taskManager, "/echotasks");
        app.MapHttpA2A(taskManager, "/echotasks");
        break;

    case "researcher":
        var researcherAgent = new ResearcherAgent();
        researcherAgent.Attach(taskManager);
        app.MapA2A(taskManager, "/researcher");
        break;

    default:
        Console.WriteLine($"Unknown agent type: {agentType}");
        Console.WriteLine("Available agents: echo, echotasks, researcher");
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
    Console.WriteLine("No agent specified. Use --agent or -a parameter to specify agent type (echo, echotasks, researcher). Defaulting to 'echo'.");
    return "echo";
}