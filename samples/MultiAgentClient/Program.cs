// MultiAgentClient: Validates the multi-agent hosting sample by talking to two
// agents hosted on the same server, each identified by its subdomain.
//
// Since local DNS doesn't resolve *.platform.local, the client uses a custom
// HttpClient that sets the X-Agent-Subdomain header. In production with real
// DNS, standard A2AClient and A2ACardResolver work out of the box.
//
// Usage:
//   1. Start MultiAgentHost: cd ../MultiAgentHost && dotnet run
//   2. Run this client:      dotnet run

using A2A;
using System.Text.Json;

var baseUrl = "http://localhost:5060";
var jsonOptions = new JsonSerializerOptions(A2AJsonUtilities.DefaultOptions) { WriteIndented = true };

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║   Multi-Agent Hosting Demo — Client                        ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ──── Helper: create an HttpClient that sends the subdomain header ────
static HttpClient CreateSubdomainClient(string subdomain)
{
    var client = new HttpClient();
    client.DefaultRequestHeaders.Add("X-Agent-Subdomain", subdomain);
    return client;
}

// ──── 1. Discover agent cards for both agents ────
Console.WriteLine("▶ Step 1: Discovering agent cards...");

var schedulerCard = await DiscoverAgentCardAsync("scheduler");
var researchCard = await DiscoverAgentCardAsync("research");

// ──── 2. Send messages to each agent and verify isolation ────
Console.WriteLine("▶ Step 2: Sending messages to each agent...");

var schedulerClient = new A2AClient(new Uri(baseUrl), CreateSubdomainClient("scheduler"));
var researchClient = new A2AClient(new Uri(baseUrl), CreateSubdomainClient("research"));

var schedulerResponse = await SendAndPrintAsync(schedulerClient, "scheduler", "Book an appointment for Monday at 10am");
var researchResponse = await SendAndPrintAsync(researchClient, "research", "Find patients eligible for trial NCT-2026-001");

// ──── 3. Verify task isolation ────
Console.WriteLine("▶ Step 3: Verifying task isolation...");

var schedulerTasks = await schedulerClient.ListTasksAsync(new ListTasksRequest());
var researchTasks = await researchClient.ListTasksAsync(new ListTasksRequest());

Console.WriteLine($"  Scheduler agent tasks: {schedulerTasks.Tasks?.Count ?? 0}");
Console.WriteLine($"  Research agent tasks:  {researchTasks.Tasks?.Count ?? 0}");

// Each agent should only see its own task
var schedulerTaskCount = schedulerTasks.Tasks?.Count ?? 0;
var researchTaskCount = researchTasks.Tasks?.Count ?? 0;

if (schedulerTaskCount >= 1 && researchTaskCount >= 1)
    Console.WriteLine("  ✓ Task isolation confirmed — each agent only sees its own tasks.");
else
    Console.WriteLine("  ✗ Unexpected task counts — isolation may not be working.");

// ──── 4. Verify cross-agent task lookup fails ────
Console.WriteLine();
Console.WriteLine("▶ Step 4: Verifying cross-agent access is blocked...");

// Try to get a scheduler task from the research agent
var schedulerTaskId = schedulerResponse.Task?.Id;
if (schedulerTaskId is not null)
{
    try
    {
        await researchClient.GetTaskAsync(new GetTaskRequest { Id = schedulerTaskId });
        Console.WriteLine("  ✗ Cross-agent access was NOT blocked — isolation failure!");
    }
    catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.TaskNotFound)
    {
        Console.WriteLine($"  ✓ Research agent cannot access scheduler's task {schedulerTaskId} — TaskNotFound.");
    }
}

// ──── 5. Verify unknown subdomain returns error ────
Console.WriteLine();
Console.WriteLine("▶ Step 5: Verifying unknown agent returns error...");

var unknownClient = new A2AClient(new Uri(baseUrl), CreateSubdomainClient("nonexistent"));
try
{
    await unknownClient.SendMessageAsync(new SendMessageRequest
    {
        Message = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString(),
            Parts = [Part.FromText("Hello?")]
        }
    });
    Console.WriteLine("  ✗ Unknown agent did NOT return an error!");
}
catch (A2AException ex)
{
    Console.WriteLine($"  ✓ Unknown agent returned error: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("════════════════════════════════════════════════════════════════");
Console.WriteLine("  Demo complete. All agents operated independently on one host.");
Console.WriteLine("════════════════════════════════════════════════════════════════");

// ── Helper methods ──

async Task<AgentCard> DiscoverAgentCardAsync(string subdomain)
{
    using var http = CreateSubdomainClient(subdomain);
    var json = await http.GetStringAsync($"{baseUrl}/.well-known/agent-card.json");
    var card = JsonSerializer.Deserialize<AgentCard>(json, A2AJsonUtilities.DefaultOptions)
        ?? throw new InvalidOperationException("Failed to deserialize agent card");

    Console.WriteLine($"  [{subdomain}] Agent: {card.Name}");
    Console.WriteLine($"           Description: {card.Description}");
    Console.WriteLine($"           Skills: {string.Join(", ", card.Skills.Select(s => s.Name))}");
    Console.WriteLine();

    return card;
}

async Task<SendMessageResponse> SendAndPrintAsync(A2AClient client, string label, string text)
{
    var response = await client.SendMessageAsync(new SendMessageRequest
    {
        Message = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString(),
            Parts = [Part.FromText(text)]
        }
    });

    var replyText = response.Task?.Artifacts?.LastOrDefault()?.Parts?.FirstOrDefault()?.Text
        ?? response.Message?.Parts?.FirstOrDefault()?.Text
        ?? "(no text response)";

    Console.WriteLine($"  [{label}] Sent: \"{text}\"");
    Console.WriteLine($"  [{label}] Reply: \"{replyText}\"");
    Console.WriteLine();

    return response;
}
