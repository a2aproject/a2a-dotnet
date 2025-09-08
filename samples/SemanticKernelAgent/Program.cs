using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SemanticKernelAgent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient()
    .AddLogging()
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService("TravelAgent");
    })
    .WithTracing(tracing => tracing
        .AddSource(TaskManager.ActivitySource.Name)
        .AddSource(A2AJsonRpcProcessor.ActivitySource.Name)
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
var taskManager = new TaskManager();
var agentCardProvider = new AgentCardProvider();
agent.Attach(agentCardProvider, taskManager);
app.MapA2A(taskManager, agentCardProvider, string.Empty);
app.MapHttpA2A(taskManager, agentCardProvider, string.Empty);

await app.RunAsync();
