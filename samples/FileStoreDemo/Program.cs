// FileStoreDemo: Demonstrates file-backed event store with data recovery after server restart.
//
// This demo:
// 1. Starts an A2A server with FileEventStore (persists to ./demo-data/)
// 2. Sends messages that create tasks with artifacts
// 3. Lists tasks — shows they exist
// 4. Stops the server (simulating a crash/restart)
// 5. Starts a NEW server with FileEventStore pointing to the same directory
// 6. Lists tasks again — shows data survived the restart
// 7. Retrieves individual task details — full history and artifacts preserved
//
// Usage: dotnet run

using A2A;
using A2A.AspNetCore;
using AgentServer;

var baseUrl = "http://localhost:5099";
var agentPath = "/demo";
var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "demo-data");

// Clean up from previous runs
if (Directory.Exists(dataDir))
    Directory.Delete(dataDir, recursive: true);

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║    FileEventStore Demo — Data Recovery After Restart        ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ───── Phase 1: Start server, create tasks ─────

Console.WriteLine("▶ Phase 1: Starting server with FileEventStore...");
var (host1, client) = await StartServerAsync(baseUrl, agentPath, dataDir);

Console.WriteLine("  Sending 3 messages to create tasks...");

var task1Response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message
    {
        Role = Role.User,
        MessageId = Guid.NewGuid().ToString(),
        ContextId = "project-alpha",
        Parts = [Part.FromText("Analyze the quarterly report for Q4 2025")],
    }
});
var task1Id = task1Response.Task?.Id ?? task1Response.Message?.TaskId ?? "(no task)";
Console.WriteLine($"  ✓ Task 1 created: {task1Id}");

var task2Response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message
    {
        Role = Role.User,
        MessageId = Guid.NewGuid().ToString(),
        ContextId = "project-alpha",
        Parts = [Part.FromText("Summarize the key findings from the analysis")],
    }
});
var task2Id = task2Response.Task?.Id ?? task2Response.Message?.TaskId ?? "(no task)";
Console.WriteLine($"  ✓ Task 2 created: {task2Id}");

var task3Response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message
    {
        Role = Role.User,
        MessageId = Guid.NewGuid().ToString(),
        ContextId = "project-beta",
        Parts = [Part.FromText("Draft a proposal for the new initiative")],
    }
});
var task3Id = task3Response.Task?.Id ?? task3Response.Message?.TaskId ?? "(no task)";
Console.WriteLine($"  ✓ Task 3 created: {task3Id}");

// List tasks before restart
Console.WriteLine();
Console.WriteLine("  Listing all tasks before restart:");
var listBefore = await client.ListTasksAsync(new ListTasksRequest());
Console.WriteLine($"  Found {listBefore.Tasks?.Count ?? 0} tasks (totalSize: {listBefore.TotalSize})");
foreach (var task in listBefore.Tasks ?? [])
{
    Console.WriteLine($"    - {task.Id}: status={task.Status.State}, context={task.ContextId}");
}

// List with context filter
Console.WriteLine();
Console.WriteLine("  Listing tasks for context 'project-alpha':");
var alphaTasksBefore = await client.ListTasksAsync(new ListTasksRequest { ContextId = "project-alpha" });
Console.WriteLine($"  Found {alphaTasksBefore.Tasks?.Count ?? 0} tasks");

// ───── Phase 2: Stop server (simulate restart) ─────

Console.WriteLine();
Console.WriteLine("▶ Phase 2: Stopping server (simulating crash/restart)...");
await host1.StopAsync();
await host1.DisposeAsync();
Console.WriteLine("  ✓ Server stopped. Data persisted at: " + dataDir);

// Show what's on disk
Console.WriteLine();
Console.WriteLine("  Files on disk:");
foreach (var file in Directory.GetFiles(dataDir, "*.*", SearchOption.AllDirectories))
{
    var relativePath = Path.GetRelativePath(dataDir, file);
    var size = new FileInfo(file).Length;
    Console.WriteLine($"    {relativePath} ({size} bytes)");
}

// ───── Phase 3: Start NEW server, verify data recovery ─────

Console.WriteLine();
Console.WriteLine("▶ Phase 3: Starting NEW server with same data directory...");
var (host2, client2) = await StartServerAsync(baseUrl, agentPath, dataDir);

Console.WriteLine("  Listing all tasks after restart:");
var listAfter = await client2.ListTasksAsync(new ListTasksRequest());
Console.WriteLine($"  Found {listAfter.Tasks?.Count ?? 0} tasks (totalSize: {listAfter.TotalSize})");
foreach (var task in listAfter.Tasks ?? [])
{
    Console.WriteLine($"    - {task.Id}: status={task.Status.State}, context={task.ContextId}");
}

// Verify context filter still works
Console.WriteLine();
Console.WriteLine("  Listing tasks for context 'project-alpha' after restart:");
var alphaTasksAfter = await client2.ListTasksAsync(new ListTasksRequest { ContextId = "project-alpha" });
Console.WriteLine($"  Found {alphaTasksAfter.Tasks?.Count ?? 0} tasks");

// Get individual task details
Console.WriteLine();
Console.WriteLine("  Getting task details after restart:");
try
{
    var details = await client2.GetTaskAsync(new GetTaskRequest { Id = task1Id });
    Console.WriteLine($"    Task {task1Id}:");
    Console.WriteLine($"      Status: {details.Status.State}");
    Console.WriteLine($"      ContextId: {details.ContextId}");
    Console.WriteLine($"      History: {details.History?.Count ?? 0} messages");
    if (details.History is { Count: > 0 })
    {
        foreach (var msg in details.History)
        {
            var text = msg.Parts?.FirstOrDefault()?.Text ?? "(no text)";
            Console.WriteLine($"        [{msg.Role}] {text}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"    Error: {ex.Message}");
}

// ───── Phase 4: Send a follow-up to an existing task ─────

Console.WriteLine();
Console.WriteLine("▶ Phase 4: Sending follow-up message to existing task...");
try
{
    var followUp = await client2.SendMessageAsync(new SendMessageRequest
    {
        Message = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString(),
            TaskId = task1Id,
            Parts = [Part.FromText("Can you also include the revenue breakdown?")],
        }
    });
    Console.WriteLine($"  ✓ Follow-up sent to task {task1Id}");

    var updatedTask = await client2.GetTaskAsync(new GetTaskRequest { Id = task1Id });
    Console.WriteLine($"  History now has {updatedTask.History?.Count ?? 0} messages");
}
catch (A2AException ex) when (ex.ErrorCode == A2AErrorCode.UnsupportedOperation)
{
    Console.WriteLine($"  ✓ Follow-up correctly rejected: {ex.Message}");
    Console.WriteLine("    (Task is completed — spec requires rejecting messages to terminal tasks)");
}

// ───── Done ─────

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  ✓ Demo complete — data survived server restart!            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

await host2.StopAsync();
await host2.DisposeAsync();

// Clean up demo data
if (Directory.Exists(dataDir))
    Directory.Delete(dataDir, recursive: true);

return;

// ─── Helper: start an A2A server with FileEventStore ───

static async Task<(WebApplication host, A2AClient client)> StartServerAsync(
    string baseUrl, string agentPath, string dataDir)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls(baseUrl);

    // Register FileEventStore BEFORE AddA2AAgent (TryAddSingleton picks up ours)
    builder.Services.AddSingleton<ITaskEventStore>(new FileEventStore(dataDir));
    builder.Services.AddA2AAgent<DemoAgent>(DemoAgent.GetAgentCard($"{baseUrl}{agentPath}"));

    // Suppress noisy console output
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    var app = builder.Build();
    app.MapA2A(agentPath);

    await app.StartAsync();

    var client = new A2AClient(new Uri($"{baseUrl}{agentPath}"));
    return (app, client);
}

// ─── Simple demo agent ───

file sealed class DemoAgent : IAgentHandler
{
    public static AgentCard GetAgentCard(string url) => new()
    {
        Name = "File Store Demo Agent",
        Description = "Demonstrates FileEventStore with data persistence across restarts.",
        Version = "1.0.0",
        SupportedInterfaces =
        [
            new AgentInterface { Url = url, ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }
        ],
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills = [new AgentSkill { Id = "demo", Name = "Demo" }],
        DefaultInputModes = ["text"],
        DefaultOutputModes = ["text"],
    };

    public async Task ExecuteAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken ct)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(ct);
        await updater.StartWorkAsync(cancellationToken: ct);

        // Echo back with a response
        var userText = context.Message.Parts?.FirstOrDefault()?.Text ?? "no input";
        await eventQueue.EnqueueMessageAsync(new Message
        {
            Role = Role.Agent,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = context.ContextId,
            TaskId = context.TaskId,
            Parts = [Part.FromText($"Acknowledged: {userText}")],
        }, ct);

        await updater.CompleteAsync(cancellationToken: ct);
    }

    public async Task CancelAsync(AgentContext context, AgentEventQueue eventQueue, CancellationToken ct)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.CancelAsync(ct);
    }
}
