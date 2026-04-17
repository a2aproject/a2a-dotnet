// MultiAgentHost: Demonstrates subdomain-based multi-agent hosting on a single server.
//
// Architecture:
//   - Each agent is identified by a subdomain (e.g., scheduler.platform.local)
//   - A2AServerFactory creates and caches an A2AServer per subdomain, each with its own InMemoryTaskStore
//   - SubdomainMiddleware extracts the subdomain from the Host header (or X-Agent-Subdomain header for testing)
//   - MultiAgentHandler delegates IA2ARequestHandler calls to the correct A2AServer
//   - Agent cards are served dynamically per subdomain at /.well-known/agent-card.json
//
// For local testing without DNS, use the X-Agent-Subdomain header:
//   curl -H "X-Agent-Subdomain: scheduler" http://localhost:5060/.well-known/agent-card.json
//
// Usage: dotnet run

using A2A;
using A2A.AspNetCore;
using MultiAgentHost;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SubdomainContext>();
builder.Services.AddSingleton<A2AServerFactory>();

var app = builder.Build();

// Configure agents in the factory
var factory = app.Services.GetRequiredService<A2AServerFactory>();

factory.Register(new AgentRegistration(
    Subdomain: "scheduler",
    AgentName: "Scheduler Agent",
    Description: "Manages appointments for healthcare providers.",
    Skills:
    [
        new AgentSkill { Id = "add-appointment", Name = "Add Appointment", Description = "Schedule a new appointment.", Tags = ["scheduling"] },
        new AgentSkill { Id = "cancel-appointment", Name = "Cancel Appointment", Description = "Cancel an existing appointment.", Tags = ["scheduling"] },
    ],
    Handler: new NamedEchoAgent("Scheduler Agent")));

factory.Register(new AgentRegistration(
    Subdomain: "research",
    AgentName: "Clinical Trials Agent",
    Description: "Finds patients matching clinical trial criteria.",
    Skills:
    [
        new AgentSkill { Id = "match-trial", Name = "Match Trial", Description = "Match patients to clinical trials.", Tags = ["clinical", "research"] },
        new AgentSkill { Id = "read-patient", Name = "Read Patient Data", Description = "Read patient information.", Tags = ["clinical", "data"] },
    ],
    Handler: new NamedEchoAgent("Clinical Trials Agent")));

// The base domain used to extract subdomains from Host header.
// e.g., "scheduler.platform.local" with baseDomain ".platform.local" → "scheduler"
var baseDomain = app.Configuration["BaseDomain"] ?? ".platform.local";

// Subdomain extraction middleware — must run before routing
app.UseMiddleware<SubdomainMiddleware>(baseDomain);

// Dynamic agent card endpoint — returns per-subdomain agent card
app.MapGet(".well-known/agent-card.json", (HttpContext ctx) =>
{
    var subdomainCtx = ctx.RequestServices.GetRequiredService<SubdomainContext>();
    if (string.IsNullOrEmpty(subdomainCtx.Subdomain))
        return Results.NotFound("No agent subdomain resolved. Use a subdomain or set the X-Agent-Subdomain header.");

    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var card = factory.GetAgentCard(subdomainCtx.Subdomain, baseUrl);
    return card is not null ? Results.Ok(card) : Results.NotFound($"Unknown agent: '{subdomainCtx.Subdomain}'");
});

// A2A JSON-RPC endpoint — single path, routed to the correct agent by MultiAgentHandler
var multiHandler = new MultiAgentHandler(
    factory,
    app.Services.GetRequiredService<IHttpContextAccessor>());

app.MapA2A(multiHandler, "/");

// Health check
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Agents = factory.Subdomains }));

Console.WriteLine();
Console.WriteLine("Multi-Agent Host started.");
Console.WriteLine("Registered agents:");
foreach (var subdomain in factory.Subdomains)
{
    Console.WriteLine($"  - {subdomain}.platform.local → http://localhost:5060 (use X-Agent-Subdomain: {subdomain})");
}
Console.WriteLine();
Console.WriteLine("Test with:");
Console.WriteLine("  curl -H \"X-Agent-Subdomain: scheduler\" http://localhost:5060/.well-known/agent-card.json");
Console.WriteLine("  curl -H \"X-Agent-Subdomain: research\" http://localhost:5060/.well-known/agent-card.json");
Console.WriteLine();

app.Run();
