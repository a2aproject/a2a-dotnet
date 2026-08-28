// This sample shows how to host a travel agent and expose it through the A2A protocol.

using System.ClientModel;
using A2A;
using A2A.AspNetCore;
using A2AServer;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

// Create the ASP.NET Core host.
var builder = WebApplication.CreateBuilder(args);

// Read the OpenAI model and agent URL configuration.
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("OPENAI_API_KEY is not set.");
var model = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL_NAME") ?? "gpt-5.4-mini";
var agentUrl = Environment.GetEnvironmentVariable("A2A_AGENT_URL") ?? "http://localhost:5000";

const string TravelAgentName = "TravelAgent";

// Create a Responses-based Agent Framework agent and provide its tools.
AIAgent travelAgent = new ResponsesClient(new ApiKeyCredential(apiKey))
    .AsAIAgent(
        model: model,
        name: TravelAgentName,
        instructions:
            """
            You specialize in planning and recommending activities for travelers.
            This includes suggesting sightseeing options, local events, dining recommendations,
            booking tickets for attractions, advising on travel itineraries, and ensuring activities
            align with traveler preferences and schedule.
            Use GetAvailableTour to retrieve a country's tour from the local catalog instead of inventing availability.
            Use GetCurrentLocalTime when the current time at a destination is relevant.
            Your goal is to create enjoyable and personalized experiences for travelers.
            """,
        tools:
        [
            AIFunctionFactory.Create(TravelTools.GetAvailableTour),
            AIFunctionFactory.Create(TravelTools.GetCurrentLocalTime),
        ]);

// Create the agent card published at the well-known discovery endpoint.
AgentCard travelAgentCard = TravelAgentCard.Create(agentUrl);

// Preserve Agent Framework sessions across requests with the same A2A context.
builder.Services.AddKeyedSingleton<AgentSessionStore>(
    TravelAgentName,
    new InMemoryAgentSessionStore());

// Register the travel agent with the A2A hosting services.
builder.AddA2AServer(travelAgent);

var app = builder.Build();

// Expose the agent through both supported A2A protocol bindings.
var a2aServer = app.Services.GetRequiredKeyedService<A2A.A2AServer>(TravelAgentName);
app.MapA2A(a2aServer, "/");
app.MapHttpA2A(a2aServer, "/");

// Publish the agent card at the well-known discovery endpoint.
app.MapWellKnownAgentCard(travelAgentCard);

await app.RunAsync();
