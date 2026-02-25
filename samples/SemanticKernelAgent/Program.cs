using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SemanticKernelAgent;

using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var agentUrl = "http://localhost:5000";

// Register the SK Travel Agent — constructor needs IConfiguration, HttpClient, ILogger
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAgentHandler>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<SemanticKernelTravelAgent>();
    return new SemanticKernelTravelAgent(configuration, httpClient, logger);
});
builder.Services.AddSingleton(SemanticKernelTravelAgent.GetAgentCard(agentUrl));
builder.Services.AddSingleton(new A2AServerOptions());
builder.Services.TryAddSingleton<ChannelEventNotifier>();
builder.Services.TryAddSingleton<ITaskEventStore>(sp =>
    new InMemoryEventStore(sp.GetRequiredService<ChannelEventNotifier>()));
builder.Services.TryAddSingleton<IEventStore>(sp =>
    sp.GetRequiredService<ITaskEventStore>());
builder.Services.TryAddSingleton<IEventSubscriber>(sp =>
    new ChannelEventSubscriber(
        sp.GetRequiredService<IEventStore>(),
        sp.GetRequiredService<ChannelEventNotifier>()));
builder.Services.TryAddSingleton<IA2ARequestHandler>(sp =>
    new A2AServer(
        sp.GetRequiredService<IAgentHandler>(),
        sp.GetRequiredService<ITaskEventStore>(),
        sp.GetRequiredService<IEventSubscriber>(),
        sp.GetRequiredService<ILogger<A2AServer>>(),
        sp.GetRequiredService<A2AServerOptions>()));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("TravelAgent"))
    .WithTracing(tracing => tracing
        .AddSource("A2A")
        .AddSource("A2A.AspNetCore")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
            options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        })
     );

var app = builder.Build();
app.MapA2A("/");

await app.RunAsync();
