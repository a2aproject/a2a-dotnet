using A2A;
using A2A.AspNetCore;
using AgentServer;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure CORS to allow all origins
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Common local dev server ports: React (3000), ASP.NET (5000), Vite (5173)
        policy.WithOrigins("http://localhost:3000", "http://localhost:5000", "http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

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
app.UseCors();

// Add health endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow }));

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
        app.MapWellKnownAgentCard(taskManager, "/echo");
        app.MapHttpA2A(taskManager, "/echo");
        break;

    case "echotasks":
        var echoAgentWithTasks = new EchoAgentWithTasks();
        echoAgentWithTasks.Attach(taskManager);
        app.MapA2A(taskManager, "/echotasks");
        app.MapWellKnownAgentCard(taskManager, "/echotasks");
        app.MapHttpA2A(taskManager, "/echotasks");
        break;

    case "researcher":
        var researcherAgent = new ResearcherAgent();
        researcherAgent.Attach(taskManager);
        app.MapA2A(taskManager, "/researcher");
        app.MapWellKnownAgentCard(taskManager, "/researcher");
        break;

    case "speccompliance":
        var specComplianceAgent = new SpecComplianceAgent();
        specComplianceAgent.Attach(taskManager);
        app.MapA2A(taskManager, "/speccompliance");
        app.MapWellKnownAgentCard(taskManager, "/speccompliance");
        break;

    case "streaming":
        var streamingAgent = new StreamingArtifactAgent();
        streamingAgent.Attach(taskManager);
        app.MapA2A(taskManager, "/streaming");
        app.MapWellKnownAgentCard(taskManager, "/streaming");
        app.MapHttpA2A(taskManager, "/streaming");
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
    Console.WriteLine("No agent specified. Use --agent or -a parameter to specify agent type (echo, echotasks, researcher, speccompliance, streaming). Defaulting to 'echo'.");
    return "echo";
}