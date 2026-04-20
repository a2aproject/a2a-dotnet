# TaskManager Migration Guide: v0.3 to v1

This guide helps you migrate existing A2A agent implementations from the v0.3
`TaskManager` callback patterns to the v1 API.

## Overview of changes

v1 replaces the `TaskManager` callback model with `IAgentHandler` — a single
interface where your agent owns its execution logic. The SDK handles task
lifecycle, persistence, and event streaming through `A2AServer` behind the scenes.

| Aspect | v0.3 | v1 |
|--------|------|-----|
| Agent interface | Callback delegates (`OnMessageReceived`, `OnTaskCreated`, etc.) | `IAgentHandler.ExecuteAsync(RequestContext, AgentEventQueue, CancellationToken)` |
| Task creation | Automatic (TaskManager creates tasks) | Agent controls via `TaskUpdater.SubmitAsync()` |
| Status updates | `taskManager.UpdateStatusAsync(taskId, state, message)` | `TaskUpdater.StartWorkAsync()`, `CompleteAsync()`, `FailAsync()`, etc. |
| Artifacts | `taskManager.ReturnArtifactAsync(taskId, artifact)` | `TaskUpdater.AddArtifactAsync(parts)` |
| Agent card | `OnAgentCardQuery` callback | `MapWellKnownAgentCard(agentCard)` static registration |
| DI registration | Manual: `new TaskManager()` + `agent.Attach(tm)` | `services.AddA2AAgent<T>(agentCard)` |
| Endpoint mapping | `app.MapA2A(taskManager, "/agent")` | `app.MapA2A("/agent")` (DI-resolved) |
| Store interface | 6 methods (mutations + push config) | 4 methods (Get, Save, Delete, List) |
| Multi-turn routing | Separate `OnTaskCreated` (new) vs `OnTaskUpdated` (existing) | Single `ExecuteAsync` — check `context.IsContinuation` |
| Streaming | Framework-managed (`TaskUpdateEventEnumerator`) | Agent-controlled (`AgentEventQueue` + `TaskUpdater`) |

## Step-by-step migration

### Step 1: Update DI registration and endpoint wiring

**v0.3:**

```csharp
var taskManager = new TaskManager();
agent.Attach(taskManager);
app.MapA2A(taskManager, "/agent");
```

**v1:**

```csharp
using A2A;
using A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Register the agent, its card, and all A2A services via DI
builder.Services.AddA2AAgent<EchoAgent>(EchoAgent.GetAgentCard("http://localhost:5000/echo"));

var app = builder.Build();

// Map JSON-RPC endpoint
app.MapA2A("/echo");

// Map well-known agent card for discovery
var card = app.Services.GetRequiredService<AgentCard>();
app.MapWellKnownAgentCard(card);

app.Run();
```

Key differences:

- `AddA2AAgent<THandler>(agentCard)` registers `IAgentHandler`, `AgentCard`,
  `A2AServerOptions`, `ChannelEventNotifier`, `ITaskStore` (defaults to
  `InMemoryTaskStore`), and `IA2ARequestHandler` (as `A2AServer`).
- Your agent implements `IAgentHandler` instead of attaching callbacks to
  `TaskManager`.
- `MapA2A(path)` resolves the handler from DI — no need to pass the handler
  explicitly.
- To use a custom `ITaskStore`, register it before calling
  `AddA2AAgent<T>()` (uses `TryAddSingleton`).
- Agent card is served at `.well-known/agent-card.json` via
  `MapWellKnownAgentCard()`.

### Step 2: Replace OnAgentCardQuery

**v0.3:**

```csharp
taskManager.OnAgentCardQuery = (url, ct) =>
    Task.FromResult(new AgentCard { Name = "My Agent", Url = url, ... });
```

**v1:**

```csharp
// Define the agent card (typically as a static method on the agent class)
var card = new AgentCard
{
    Name = "My Agent",
    Description = "Agent description",
    Version = "1.0.0",
    SupportedInterfaces = [new AgentInterface
    {
        Url = "http://localhost:5000/agent",
        ProtocolBinding = "JSONRPC",
        ProtocolVersion = "1.0"
    }],
    DefaultInputModes = ["text/plain"],
    DefaultOutputModes = ["text/plain"],
    Capabilities = new AgentCapabilities { Streaming = false },
    Skills = [new AgentSkill { Id = "main", Name = "Main", Description = "...", Tags = ["main"] }],
};

// Register at startup
app.MapWellKnownAgentCard(card);
```

### Step 3: Migrate simple message-only agents

If your v0.3 agent used `OnMessageReceived` and returned a direct response
without creating tasks, migration is straightforward.

**v0.3:**

```csharp
taskManager.OnMessageReceived = async (msgParams, ct) =>
{
    var text = msgParams.Message.Parts.OfType<TextPart>().First().Text;
    return new AgentMessage
    {
        Role = MessageRole.Agent,
        MessageId = Guid.NewGuid().ToString(),
        ContextId = msgParams.Message.ContextId,
        Parts = [new TextPart { Text = $"Echo: {text}" }]
    };
};
```

**v1:**

```csharp
public sealed class EchoAgent : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        await responder.ReplyAsync($"Echo: {context.UserText}", cancellationToken);
    }
}
```

What changed:

- Callback delegate → implement `IAgentHandler.ExecuteAsync()`
- `MessageSendParams` → `RequestContext` (pre-resolved with `UserText`,
  `ContextId`, etc.)
- `AgentMessage` with manual field construction →
  `MessageResponder.ReplyAsync()` (auto-generates `MessageId`, sets
  `Role.Agent`)
- `TextPart { Text = ... }` → `Part.FromText(...)` (handled internally by
  `MessageResponder`)
- No return value — agent writes to `AgentEventQueue` instead of returning a
  response

### Step 4: Migrate task-based agents

If your v0.3 agent used the automatic task lifecycle (`OnTaskCreated`,
`OnTaskUpdated`, `UpdateStatusAsync`, `ReturnArtifactAsync`), you need to
restructure.

**v0.3:**

```csharp
// Agent received callbacks at different lifecycle stages
taskManager.OnTaskCreated = async (task, ct) =>
{
    // Start processing
    await taskManager.UpdateStatusAsync(task.Id, TaskState.Working, null, false, ct);

    // Do work...
    var result = await DoWorkAsync(task, ct);

    // Return artifact
    await taskManager.ReturnArtifactAsync(task.Id, new Artifact
    {
        ArtifactId = "result",
        Parts = [new TextPart { Text = result }]
    });

    // Mark complete
    await taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, null, true, ct);
};

taskManager.OnTaskCancelled = async (task, ct) =>
{
    // Cleanup...
};
```

**v1:**

```csharp
public sealed class MyAgent : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        // Signal task accepted
        await updater.SubmitAsync(cancellationToken);

        // Transition to working
        await updater.StartWorkAsync(cancellationToken: cancellationToken);

        // Do work...
        var result = await DoWorkAsync(context, cancellationToken);

        // Add result artifact
        await updater.AddArtifactAsync(
            [Part.FromText(result)], cancellationToken: cancellationToken);

        // Mark complete (also closes the event queue)
        await updater.CompleteAsync(cancellationToken: cancellationToken);
    }

    // CancelAsync has a default implementation that transitions to Canceled.
    // Override only if you need custom cleanup logic:
    public async Task CancelAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        // Custom cleanup...
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.CancelAsync(cancellationToken);
    }
}
```

What changed:

- No more `OnTaskCreated`/`OnTaskUpdated` callbacks — the single
  `ExecuteAsync` handles everything
- `taskManager.UpdateStatusAsync(taskId, state, msg, final)` → `TaskUpdater`
  methods: `SubmitAsync()`, `StartWorkAsync()`, `CompleteAsync()`,
  `FailAsync()`, `CancelAsync()`, `RequireInputAsync()`, `RejectAsync()`
- `taskManager.ReturnArtifactAsync(taskId, artifact)` →
  `updater.AddArtifactAsync(parts)`
- You don't create `AgentTask` objects directly — the SDK constructs them
  from events via `TaskProjection.Apply`
- `OnTaskCancelled` → `IAgentHandler.CancelAsync()` (has a sensible default
  implementation)
- The `TaskUpdater` manages task/context IDs — they come from
  `RequestContext` which is pre-populated by `A2AServer`

### Step 5: Migrate multi-turn / stateful agents

For agents that maintain conversation state across multiple messages:

**v0.3:**

```csharp
taskManager.OnTaskUpdated = async (task, ct) =>
{
    // TaskManager auto-appended the new message to task.History
    var lastMessage = task.History!.Last();
    // Process the follow-up message...
    await taskManager.UpdateStatusAsync(task.Id, TaskState.Completed);
};
```

**v1:**

```csharp
public sealed class ResearcherAgent : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        if (!context.IsContinuation)
        {
            // New task — initial processing
            await updater.SubmitAsync(cancellationToken);
            await updater.AddArtifactAsync(
                [Part.FromText($"{context.UserText} received.")],
                cancellationToken: cancellationToken);

            // Ask the user for more input
            await updater.RequireInputAsync(new Message
            {
                Role = Role.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = updater.ContextId,
                Parts = [Part.FromText("When ready say go ahead")],
            }, cancellationToken);
            return;
        }

        // Continuation — the user sent a follow-up message
        await updater.StartWorkAsync(cancellationToken: cancellationToken);
        await updater.AddArtifactAsync(
            [Part.FromText($"{context.UserText} received.")],
            cancellationToken: cancellationToken);
        await updater.CompleteAsync(
            new Message
            {
                Role = Role.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                Parts = [Part.FromText("Task completed successfully")],
            },
            cancellationToken);
    }
}
```

What changed:

- v0.3 auto-routed new messages to `OnTaskCreated` and follow-ups to
  `OnTaskUpdated`. In v1, the single `ExecuteAsync` callback handles both —
  check `context.IsContinuation`.
- `context.Task` contains the existing task (with history) when
  `IsContinuation` is `true`.
- `RequireInputAsync(message)` replaces manually setting `InputRequired`
  state — it transitions the task and closes the event queue.
- No manual `ITaskStore` interaction needed. The SDK automatically persists
  state via `TaskProjection.Apply`.

### Step 6: Migrate streaming agents

**v0.3:**

```csharp
// TaskManager handled streaming internally via TaskUpdateEventEnumerator
// Agent pushed events through taskManager methods:
taskManager.OnTaskCreated = async (task, ct) =>
{
    await taskManager.UpdateStatusAsync(task.Id, TaskState.Working);
    // ... do work ...
    await taskManager.ReturnArtifactAsync(task.Id, artifact);
    await taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, null, true);
    // TaskManager routed these to the SSE stream automatically
};
```

**v1:**

```csharp
public sealed class StreamingArtifactAgent : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        await updater.SubmitAsync(cancellationToken);
        await updater.StartWorkAsync(cancellationToken: cancellationToken);

        var artifactId = Guid.NewGuid().ToString("N");

        // Chunk 1: create artifact (append: false)
        await updater.AddArtifactAsync(
            [Part.FromText("First chunk of content...")],
            artifactId: artifactId, name: "Result",
            append: false, lastChunk: false,
            cancellationToken: cancellationToken);

        // Chunk 2: append to same artifact
        await updater.AddArtifactAsync(
            [Part.FromText("More content...")],
            artifactId: artifactId,
            append: true, lastChunk: false,
            cancellationToken: cancellationToken);

        // Final chunk
        await updater.AddArtifactAsync(
            [Part.FromText("Final content.")],
            artifactId: artifactId,
            append: true, lastChunk: true,
            cancellationToken: cancellationToken);

        await updater.CompleteAsync(cancellationToken: cancellationToken);
    }
}
```

What changed:

- No separate `OnSendStreamingMessage` callback — the same `ExecuteAsync`
  handles both streaming and non-streaming. The SDK decides based on
  `context.StreamingResponse` (set by the JSON-RPC method).
- No `IAsyncEnumerable<StreamResponse>` — you write events to
  `AgentEventQueue` via `TaskUpdater` methods. The SDK handles SSE framing.
- Chunked artifacts use the `append` and `lastChunk` parameters on
  `AddArtifactAsync`. Reuse the same `artifactId` across chunks.

## Type mapping quick reference

| v0.3 type | v1 type |
|-----------|---------|
| `MessageSendParams` | `SendMessageRequest` |
| `AgentMessage` | `Message` |
| `MessageRole` | `Role` |
| `TextPart` | `Part.FromText(...)` |
| `FilePart` + `FileContent` | `Part.FromUrl(...)` or `Part.FromRaw(...)` |
| `DataPart` | `Part.FromData(...)` |
| `A2AResponse` | `SendMessageResponse` |
| `A2AEvent` | `StreamResponse` |
| `AgentTaskStatus` | `TaskStatus` |
| `TaskIdParams` | `GetTaskRequest` or `CancelTaskRequest` |
| `TaskQueryParams` | `GetTaskRequest` |
| `TaskManager` | `A2AServer` + `IAgentHandler` |
| `OnMessageReceived` / `OnTaskCreated` callbacks | `IAgentHandler.ExecuteAsync()` |
| `OnTaskCancelled` callback | `IAgentHandler.CancelAsync()` |
| `taskManager.UpdateStatusAsync()` | `TaskUpdater` methods (`SubmitAsync`, `StartWorkAsync`, `CompleteAsync`, etc.) |
| `taskManager.ReturnArtifactAsync()` | `TaskUpdater.AddArtifactAsync()` |
| `ITaskStore` (6 methods) | `ITaskStore` (4 methods: `GetTaskAsync`, `SaveTaskAsync`, `DeleteTaskAsync`, `ListTasksAsync`) |

## Common migration issues

1. **`using TaskStatus = A2A.TaskStatus;`** — Add this to files that use
   `TaskStatus`, since `System.Threading.Tasks.TaskStatus` conflicts.

2. **`Part` is no longer abstract** — Replace `switch (part) { case TextPart:
   ... }` with `switch (part.ContentCase) { case PartContentCase.Text: ... }`.

3. **AgentCard has new required fields** — `Name`, `Description`, `Version`,
   `SupportedInterfaces`, `Capabilities`, `Skills`, `DefaultInputModes`,
   `DefaultOutputModes` are all required in v1.

4. **`TaskManager` class removed** — v1 has no `TaskManager` class. Replace
   `new TaskManager()` with `services.AddA2AAgent<T>(agentCard)` for DI
   registration and implement `IAgentHandler` in your agent class.

5. **Callback delegates removed** — `OnSendMessage`, `OnSendStreamingMessage`,
   `OnCancelTask`, `OnMessageReceived`, `OnTaskCreated`, `OnTaskCancelled`, and
   `OnTaskUpdated` do not exist in v1. All agent logic goes in
   `IAgentHandler.ExecuteAsync()` and `CancelAsync()`.

6. **`ITaskStore` simplified** — v1 `ITaskStore` has 4 methods (`GetTaskAsync`,
   `SaveTaskAsync`, `DeleteTaskAsync`, `ListTasksAsync`). Methods like
   `UpdateStatusAsync`, `AppendHistoryAsync`, and `SetTaskAsync` no longer
   exist. The SDK manages task mutations internally via `TaskProjection.Apply`.

7. **No more `Final` flag on streaming** — v0.3 used
   `UpdateStatusAsync(..., final: true)` to signal end of stream. In v1, the
   stream ends when the `AgentEventQueue` is completed (handled automatically
   by `TaskUpdater.CompleteAsync()`, `FailAsync()`, etc.).
