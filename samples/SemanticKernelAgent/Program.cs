using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SemanticKernelAgent;

using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient()
    .AddLogging()
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService("TravelAgent");
    })
    .WithTracing(tracing => tracing
        .AddSource(A2AJsonRpcProcessor.ActivitySource.Name)
        .AddSource(SemanticKernelTravelAgent.ActivitySource.Name)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
            options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        })
     );
var app = builder.Build();

var configuration = app.Configuration;
var httpClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
var logger = app.Logger;

var agent = new SemanticKernelTravelAgent(configuration, httpClient, logger);
var store = new InMemoryTaskStore();
var taskManagerLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<TaskManager>();
var taskManager = new TaskManager(store, taskManagerLogger);
agent.Attach(taskManager, store);

var agentUrl = "http://localhost:5000";
app.MapA2A(taskManager, "/");
app.MapWellKnownAgentCard(SemanticKernelTravelAgent.GetAgentCard(agentUrl));

await app.RunAsync();
