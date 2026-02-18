# TaskManager Migration Guide: v0.3 to v1

This guide helps you migrate existing A2A agent implementations from the v0.3
`TaskManager` callback patterns to the v1 API.

## Overview of changes

The v1 `TaskManager` is fundamentally simpler. Instead of four lifecycle
callbacks and automatic task management, you get one main callback where you
own the entire response.

| Aspect | v0.3 | v1 |
|--------|------|-----|
| Callbacks | `OnMessageReceived`, `OnTaskCreated`, `OnTaskCancelled`, `OnTaskUpdated` | `OnSendMessage`, `OnSendStreamingMessage`, `OnCancelTask` |
| Task creation | Automatic (TaskManager creates tasks) | Manual (your code creates tasks via `ITaskStore`) |
| Status updates | `taskManager.UpdateStatusAsync(taskId, state, message)` | `store.UpdateStatusAsync(taskId, status)` directly |
| Artifacts | `taskManager.ReturnArtifactAsync(taskId, artifact)` | Set `task.Artifacts` before returning |
| Agent card | `OnAgentCardQuery` callback | `MapWellKnownAgentCard()` extension method |
| Constructor | `TaskManager(HttpClient?, ITaskStore?)` | `TaskManager(ITaskStore, ILogger<TaskManager>)` |
| Return type | `A2AResponse` (polymorphic: cast to `AgentTask`, `TaskStatusUpdateEvent`, etc.) | `SendMessageResponse` (set `.Message` or `.Task`) |

## Step-by-step migration

### Step 1: Update constructor and wiring

**v0.3:**

```csharp
var taskManager = new TaskManager();
agent.Attach(taskManager);
app.MapA2A(taskManager, "/agent");
```

**v1:**

```csharp
var store = new InMemoryTaskStore();
var logger = loggerFactory.CreateLogger<TaskManager>();
var taskManager = new TaskManager(store, logger);
agent.Attach(taskManager, store);          // pass store to agent
app.MapA2A(taskManager, "/agent");
app.MapWellKnownAgentCard(agentCard);      // agent card served separately
```

Key differences:
- `ITaskStore` and `ILogger` are required constructor parameters.
- Your agent receives the `ITaskStore` so it can read/write tasks directly.
- Agent card is no longer served via a callback; use `MapWellKnownAgentCard()`.

### Step 2: Replace OnAgentCardQuery

**v0.3:**

```csharp
taskManager.OnAgentCardQuery = (url, ct) =>
    Task.FromResult(new AgentCard { Name = "My Agent", Url = url, ... });
```

**v1:**

```csharp
// In Program.cs — static registration
var card = new AgentCard
{
    Name = "My Agent",
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
taskManager.OnSendMessage = (request, ct) =>
{
    var text = request.Message.Parts.FirstOrDefault(p => p.Text is not null)?.Text ?? "";
    var response = new Message
    {
        Role = Role.Agent,
        MessageId = Guid.NewGuid().ToString("N"),
        ContextId = request.Message.ContextId,
        Parts = [Part.FromText($"Echo: {text}")]
    };
    return Task.FromResult(new SendMessageResponse { Message = response });
};
```

What changed:
- `MessageSendParams` → `SendMessageRequest`
- `AgentMessage` → `Message`
- `MessageRole.Agent` → `Role.Agent`
- `TextPart { Text = ... }` → `Part.FromText(...)`
- Return `SendMessageResponse { Message = ... }` instead of bare `AgentMessage`

### Step 4: Migrate task-based agents

If your v0.3 agent used the automatic task lifecycle (OnTaskCreated, OnTaskUpdated,
UpdateStatusAsync, ReturnArtifactAsync), you need to restructure.

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
public void Attach(TaskManager taskManager, ITaskStore store)
{
    _store = store;
    taskManager.OnSendMessage = OnSendMessageAsync;
    taskManager.OnCancelTask = OnCancelTaskAsync;
}

private async Task<SendMessageResponse> OnSendMessageAsync(
    SendMessageRequest request, CancellationToken ct)
{
    // You create the task yourself
    var taskId = Guid.NewGuid().ToString("N");
    var contextId = request.Message.ContextId ?? Guid.NewGuid().ToString("N");

    // Do work...
    var result = await DoWorkAsync(request, ct);

    // Build the complete task with status, history, and artifacts
    var task = new AgentTask
    {
        Id = taskId,
        ContextId = contextId,
        Status = new TaskStatus
        {
            State = TaskState.Completed,
            Timestamp = DateTimeOffset.UtcNow,
        },
        History = [request.Message],
        Artifacts =
        [
            new Artifact
            {
                ArtifactId = Guid.NewGuid().ToString("N"),
                Parts = [Part.FromText(result)]
            }
        ],
    };

    // Persist
    await _store!.SetTaskAsync(task, ct);

    // Return the task directly
    return new SendMessageResponse { Task = task };
}

private async Task<AgentTask> OnCancelTaskAsync(
    CancelTaskRequest request, CancellationToken ct)
{
    var task = await _store!.GetTaskAsync(request.Id, ct)
        ?? throw new A2AException($"Task '{request.Id}' not found.", A2AErrorCode.TaskNotFound);

    return await _store.UpdateStatusAsync(task.Id, new TaskStatus
    {
        State = TaskState.Canceled,
        Timestamp = DateTimeOffset.UtcNow,
    }, ct);
}
```

What changed:
- No more `OnTaskCreated`/`OnTaskUpdated` — everything happens in `OnSendMessage`
- You create `AgentTask` objects directly instead of calling `taskManager.CreateTaskAsync()`
- You set artifacts on the task object instead of calling `taskManager.ReturnArtifactAsync()`
- You persist via `ITaskStore` directly instead of `taskManager.UpdateStatusAsync()`
- `OnCancelTask` replaces `OnTaskCancelled` (note: different signature)

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
private async Task<SendMessageResponse> OnSendMessageAsync(
    SendMessageRequest request, CancellationToken ct)
{
    // Check if this references an existing task
    if (!string.IsNullOrEmpty(request.Message.TaskId))
    {
        var existing = await _store!.GetTaskAsync(request.Message.TaskId, ct);
        if (existing is not null)
        {
            // Append the new user message
            await _store.AppendHistoryAsync(existing.Id, request.Message, ct);

            // Process the follow-up...
            var reply = new Message
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Role = Role.Agent,
                TaskId = existing.Id,
                ContextId = existing.ContextId,
                Parts = [Part.FromText("Processed your follow-up.")],
            };
            await _store.AppendHistoryAsync(existing.Id, reply, ct);

            // Update status
            await _store.UpdateStatusAsync(existing.Id, new TaskStatus
            {
                State = TaskState.Completed,
                Timestamp = DateTimeOffset.UtcNow,
            }, ct);

            var updated = await _store.GetTaskAsync(existing.Id, ct);
            return new SendMessageResponse { Task = updated };
        }
    }

    // New conversation — create initial task
    var task = new AgentTask { /* ... */ };
    await _store!.SetTaskAsync(task, ct);
    return new SendMessageResponse { Task = task };
}
```

Key insight: In v0.3, `TaskManager` routed messages to
`OnTaskCreated` (new) or `OnTaskUpdated` (existing) for you. In v1, your
single `OnSendMessage` callback handles both cases — check
`request.Message.TaskId` to distinguish them.

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
taskManager.OnSendStreamingMessage = (request, ct) =>
{
    return StreamWorkAsync(request, ct);
};

private async IAsyncEnumerable<StreamResponse> StreamWorkAsync(
    SendMessageRequest request,
    [EnumeratorCancellation] CancellationToken ct)
{
    var taskId = Guid.NewGuid().ToString("N");
    var contextId = request.Message.ContextId ?? Guid.NewGuid().ToString("N");

    // Create and persist the initial task
    var task = new AgentTask
    {
        Id = taskId,
        ContextId = contextId,
        Status = new TaskStatus
        {
            State = TaskState.Working,
            Timestamp = DateTimeOffset.UtcNow,
        },
        History = [request.Message],
    };
    await _store!.SetTaskAsync(task, ct);

    // Emit status update
    yield return new StreamResponse
    {
        StatusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Working }
        }
    };

    // Do work and emit artifact chunks
    var artifact = new Artifact
    {
        ArtifactId = Guid.NewGuid().ToString("N"),
        Parts = [Part.FromText("Partial result...")]
    };
    yield return new StreamResponse
    {
        ArtifactUpdate = new TaskArtifactUpdateEvent
        {
            TaskId = taskId,
            ContextId = contextId,
            Artifact = artifact,
            Append = true,
            LastChunk = false
        }
    };

    // Persist final state with artifacts
    task.Status = new TaskStatus
    {
        State = TaskState.Completed,
        Timestamp = DateTimeOffset.UtcNow,
    };
    task.Artifacts = [artifact];
    await _store.SetTaskAsync(task, ct);

    // Emit final task
    yield return new StreamResponse { Task = task };
}
```

What changed:
- Instead of calling `UpdateStatusAsync`/`ReturnArtifactAsync` on
  TaskManager, you `yield return` `StreamResponse` objects.
- Each `StreamResponse` uses field-presence: set `.StatusUpdate`,
  `.ArtifactUpdate`, `.Message`, or `.Task`.
- You control the stream directly via `IAsyncEnumerable<StreamResponse>`.

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

## Common migration issues

1. **`using TaskStatus = A2A.TaskStatus;`** — Add this to files that use
   `TaskStatus`, since `System.Threading.Tasks.TaskStatus` conflicts.

2. **`Part` is no longer abstract** — Replace `switch (part) { case TextPart:
   ... }` with `switch (part.ContentCase) { case PartContentCase.Text: ... }`.

3. **AgentCard has new required fields** — `Version`,
   `SupportedInterfaces`, `Capabilities`, `Skills`,
   `DefaultInputModes`, `DefaultOutputModes` are all required in v1.

4. **TaskManager constructor requires ITaskStore and ILogger** — Both are
   mandatory. Use `InMemoryTaskStore` for simple cases.

5. **No more `Final` flag on streaming** — v0.3 used
   `UpdateStatusAsync(..., final: true)` to signal end of stream. In v1 the
   stream ends when your `IAsyncEnumerable` completes.
