# A2A .NET SDK: Migration Guide — v0.3 to v1

This guide helps .NET developers upgrade from the A2A v0.3 SDK to v1. The v1 release adopts [ProtoJSON](https://protobuf.dev/programming-guides/proto3/#json) serialization conventions (SCREAMING_SNAKE_CASE enums, field-presence `oneof` patterns), replaces discriminator-based polymorphism with flat sealed types, and adds new operations like `ListTasks` and a REST API binding.

> **Note**: The `A2A.V0_3` NuGet package remains available for backward compatibility during the transition period. It will be removed in a future release.

## Migration Strategy

Follow this 3-phase approach (from the [official A2A spec](https://google.github.io/A2A/)):

### Phase 1: Compatibility Layer

Add the `A2A.V0_3` package alongside `A2A` v1. Both can coexist in the same project using different namespaces (`A2A` for v1, `A2A.V0_3` for legacy). No code changes required — existing v0.3 code continues to work.

```xml
<PackageReference Include="A2A" Version="1.0.0" />
<PackageReference Include="A2A.V0_3" Version="0.3.0" />
```

### Phase 2: Dual Support

Update your client and server code to use v1 types. Keep the `A2A.V0_3` package for any consumers that haven't migrated yet. All the changes described in this guide apply to this phase.

### Phase 3: V1-Only

Remove the `A2A.V0_3` dependency entirely. All consumers are on v1.

```xml
<PackageReference Include="A2A" Version="1.0.0" />
<!-- Remove: <PackageReference Include="A2A.V0_3" /> -->
```

---

## Part Model

The biggest structural change. V0.3 used a discriminator hierarchy with `TextPart`, `FilePart`, and `DataPart` subclasses. V1 uses a single sealed `Part` class with field-presence (`oneof`).

### V0.3

```csharp
using A2A.V0_3;

// Creating parts
var textPart = new TextPart { Text = "Hello, world!" };
var filePart = new FilePart
{
    File = new FileContent
    {
        Name = "report.pdf",
        MimeType = "application/pdf",
        Uri = "https://example.com/report.pdf"
    }
};
var dataPart = new DataPart
{
    Data = JsonDocument.Parse("""{"key": "value"}""").RootElement
};

// Consuming parts (check kind, then cast)
foreach (Part part in message.Parts)
{
    switch (part)
    {
        case TextPart tp:
            Console.WriteLine(tp.Text);
            break;
        case FilePart fp:
            Console.WriteLine(fp.File.Uri);
            break;
        case DataPart dp:
            Console.WriteLine(dp.Data);
            break;
    }
}
```

### V1

```csharp
using A2A;

// Creating parts — use factory methods
var textPart = Part.FromText("Hello, world!");
var filePart = Part.FromUrl("https://example.com/report.pdf",
    mediaType: "application/pdf", filename: "report.pdf");
var dataPart = Part.FromData(
    JsonDocument.Parse("""{"key": "value"}""").RootElement);

// Consuming parts — use ContentCase enum
foreach (Part part in message.Parts)
{
    switch (part.ContentCase)
    {
        case PartContentCase.Text:
            Console.WriteLine(part.Text);
            break;
        case PartContentCase.Url:
            Console.WriteLine(part.Url);
            break;
        case PartContentCase.Raw:
            // part.Raw is byte[] (base64 in JSON)
            break;
        case PartContentCase.Data:
            Console.WriteLine(part.Data);
            break;
    }
}
```

**Key differences:**

| Aspect | V0.3 | V1 |
|--------|------|-----|
| Type hierarchy | `TextPart`, `FilePart`, `DataPart` subclasses | Single sealed `Part` class |
| Content identification | `kind` discriminator + C# type casting | `ContentCase` computed enum |
| File content | Nested `FileContent` class with `Uri`/`Bytes` | Flat: `Part.Url` or `Part.Raw` |
| MIME type field | `MimeType` (on `FileContent`) | `MediaType` (on `Part`) |
| Factory methods | None (direct construction) | `Part.FromText()`, `Part.FromUrl()`, `Part.FromRaw()`, `Part.FromData()` |

---

## Response Types

V0.3 used `A2AResponse` with a `kind` discriminator. V1 splits into `SendMessageResponse` (for `SendMessage`) and `StreamResponse` (for streaming).

### V0.3

```csharp
// SendMessage returned an A2AResponse with kind-based discrimination
A2AResponse response = await client.SendMessageAsync(sendParams);
switch (response)
{
    case AgentTask task:
        Console.WriteLine($"Task: {task.Id}, State: {task.Status.State}");
        break;
    case TaskStatusUpdateEvent update:
        Console.WriteLine($"Status: {update.Status.State}");
        break;
}
```

### V1

```csharp
// SendMessage returns SendMessageResponse with named fields
SendMessageResponse response = await client.SendMessageAsync(request);
switch (response.PayloadCase)
{
    case SendMessageResponseCase.Task:
        Console.WriteLine($"Task: {response.Task!.Id}, State: {response.Task.Status.State}");
        break;
    case SendMessageResponseCase.Message:
        Console.WriteLine($"Message from agent: {response.Message!.Parts[0].Text}");
        break;
}
```

### Streaming

```csharp
// V0.3: IAsyncEnumerable<A2AEvent> with kind-based casting
await foreach (A2AEvent evt in client.SendStreamingAsync(params))
{
    if (evt is TaskStatusUpdateEvent statusUpdate) { ... }
    if (evt is TaskArtifactUpdateEvent artifactUpdate) { ... }
}

// V1: IAsyncEnumerable<StreamResponse> with PayloadCase
await foreach (StreamResponse evt in client.SendStreamingMessageAsync(request))
{
    switch (evt.PayloadCase)
    {
        case StreamResponseCase.StatusUpdate:
            Console.WriteLine(evt.StatusUpdate!.Status.State);
            break;
        case StreamResponseCase.ArtifactUpdate:
            Console.WriteLine(evt.ArtifactUpdate!.Artifact.ArtifactId);
            break;
        case StreamResponseCase.Task:
            Console.WriteLine(evt.Task!.Id);
            break;
        case StreamResponseCase.Message:
            Console.WriteLine(evt.Message!.Parts[0].Text);
            break;
    }
}
```

---

## SecurityScheme

V0.3 used discriminator-based polymorphism. V1 uses field-presence on a flat sealed class.

### V0.3

```csharp
// Creating security schemes
var apiKey = new SecurityScheme
{
    ApiKeySecurityScheme = new ApiKeySecurityScheme
    {
        Name = "X-API-Key",
        In = "header"  // Note: "In" property
    }
};
```

### V1

```csharp
// Creating security schemes — same structure, different field names
var apiKey = new SecurityScheme
{
    ApiKeySecurityScheme = new ApiKeySecurityScheme
    {
        Name = "X-API-Key",
        Location = "header"  // Renamed: "In" → "Location"
    }
};

// Use SchemeCase for type identification
switch (scheme.SchemeCase)
{
    case SecuritySchemeCase.ApiKey:
        Console.WriteLine(scheme.ApiKeySecurityScheme!.Location);
        break;
    case SecuritySchemeCase.OAuth2:
        Console.WriteLine(scheme.OAuth2SecurityScheme!.Flows.FlowCase);
        break;
}
```

**Key change:** `ApiKeySecurityScheme.In` is renamed to `ApiKeySecurityScheme.Location` (JSON property `"location"` to match proto).

---

## Type Renames

| V0.3 | V1 | Notes |
|------|-----|-------|
| `AgentMessage` | `Message` | Renamed |
| `AgentTaskStatus` | `TaskStatus` | Struct → sealed class |
| `MessageRole` | `Role` | Renamed |
| `TextPart` | `Part` | Unified (use `Part.FromText()`) |
| `FilePart` | `Part` | Unified (use `Part.FromUrl()` / `Part.FromRaw()`) |
| `DataPart` | `Part` | Unified (use `Part.FromData()`) |
| `A2AResponse` | `SendMessageResponse` / `StreamResponse` | Split by use case |
| `A2AEvent` | `StreamResponse` | Unified with response |
| `FileContent` | (removed) | Fields moved to `Part` directly |
| `PartKind` | `PartContentCase` | Computed enum |
| `A2AEventKind` | `StreamResponseCase` | Computed enum |

---

## JSON Wire Format

All JSON wire format changes follow the A2A v1 ProtoJSON conventions.

### Enum Values

```json
// V0.3 (kebab-case)
{ "state": "input-required" }
{ "role": "user" }

// V1 (SCREAMING_SNAKE_CASE with type prefix)
{ "state": "TASK_STATE_INPUT_REQUIRED" }
{ "role": "ROLE_USER" }
```

**Full enum mapping:**

| V0.3 | V1 |
|------|-----|
| `"submitted"` | `"TASK_STATE_SUBMITTED"` |
| `"working"` | `"TASK_STATE_WORKING"` |
| `"input-required"` | `"TASK_STATE_INPUT_REQUIRED"` |
| `"completed"` | `"TASK_STATE_COMPLETED"` |
| `"canceled"` | `"TASK_STATE_CANCELED"` |
| `"failed"` | `"TASK_STATE_FAILED"` |
| `"user"` | `"ROLE_USER"` |
| `"agent"` | `"ROLE_AGENT"` |

**New v1 enum values** (not in v0.3): `TASK_STATE_REJECTED`, `TASK_STATE_AUTH_REQUIRED`, `TASK_STATE_UNSPECIFIED`.

### Part Format

```json
// V0.3 — kind discriminator
{"kind": "text", "text": "Hello"}
{"kind": "file", "file": {"uri": "https://...", "mimeType": "text/plain"}}

// V1 — field-presence (no kind, flat structure)
{"text": "Hello"}
{"url": "https://...", "mediaType": "text/plain"}
```

### Response Format

```json
// V0.3 — kind at root level
{
  "result": {"kind": "task", "id": "abc", "status": {"state": "working"}}
}

// V1 — named wrapper
{
  "result": {"task": {"id": "abc", "status": {"state": "TASK_STATE_WORKING"}}}
}
```

### JSON-RPC Method Names

| V0.3 | V1 |
|------|-----|
| `"message/send"` | `"SendMessage"` |
| `"message/stream"` | `"SendStreamingMessage"` |
| `"tasks/get"` | `"GetTask"` |
| `"tasks/cancel"` | `"CancelTask"` |
| `"tasks/pushNotificationConfig/set"` | `"CreateTaskPushNotificationConfig"` |
| `"tasks/pushNotificationConfig/get"` | `"GetTaskPushNotificationConfig"` |
| `"tasks/resubscribe"` | `"SubscribeToTask"` |
| N/A | `"ListTasks"` (new) |
| N/A | `"DeleteTaskPushNotificationConfig"` (new) |
| N/A | `"ListTaskPushNotificationConfig"` (new) |
| N/A | `"GetExtendedAgentCard"` (new) |

---

## AgentCard Changes

### V0.3

```csharp
var card = new AgentCard
{
    Name = "My Agent",
    Description = "Does things.",
    Url = "http://localhost:5000/agent",
    // No Version field
    // protocolVersion at top level
    Skills = [new AgentSkill { Id = "skill1", Name = "Skill", Description = "Desc" }],
};
```

### V1

```csharp
var card = new AgentCard
{
    Name = "My Agent",
    Description = "Does things.",
    Version = "1.0.0",  // Required in v1
    SupportedInterfaces =
    [
        new AgentInterface
        {
            Url = "http://localhost:5000/agent",
            ProtocolBinding = "JSONRPC",
            ProtocolVersion = "1.0"
        }
    ],
    DefaultInputModes = ["text/plain"],   // Required in v1
    DefaultOutputModes = ["text/plain"],  // Required in v1
    Capabilities = new AgentCapabilities { Streaming = true },  // Required in v1
    Skills =
    [
        new AgentSkill
        {
            Id = "skill1",
            Name = "Skill",
            Description = "Description",
            Tags = ["example"]  // Required in v1
        }
    ],
};
```

**Key differences:**

| Field | V0.3 | V1 |
|-------|------|-----|
| `Url` | Top-level string | Removed (use `SupportedInterfaces[0].Url`) |
| `Version` | Not present | Required `string` |
| `SupportedInterfaces` | Not present | Required `AgentInterface[]` |
| `ProtocolVersion` | Top-level | In `AgentInterface.ProtocolVersion` |
| `Icons` | `List<AgentIcon>?` (complex type) | `string? IconUrl` |
| `DocumentationUrl` | Not present | Optional `string?` |
| `Capabilities` | Optional | Required (non-nullable) |
| `Skills` | Optional | Required (non-nullable) |
| `DefaultInputModes` | Optional | Required (non-nullable) |
| `DefaultOutputModes` | Optional | Required (non-nullable) |
| `AgentSkill.Tags` | Optional | Required (non-nullable) |

---

## Client API Changes

### Interface

```csharp
// V0.3
public interface IA2AClient : IDisposable { ... }

// V1 — IDisposable removed from interface
public interface IA2AClient { ... }

// A2AClient (concrete) still implements IDisposable directly:
public sealed class A2AClient : IA2AClient, IDisposable { ... }
```

### Parameter Types

V0.3 used inline parameter classes. V1 uses named request objects:

```csharp
// V0.3
var sendParams = new MessageSendParams
{
    Message = new AgentMessage { Role = MessageRole.User, Parts = [new TextPart { Text = "Hi" }] }
};
var response = await client.SendMessageAsync(sendParams);

// V1
var request = new SendMessageRequest
{
    Message = new Message
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.User,
        Parts = [Part.FromText("Hi")]
    }
};
var response = await client.SendMessageAsync(request);
```

### New Operations

V1 adds these operations not available in v0.3:

```csharp
// List tasks with pagination and filtering
var listResult = await client.ListTasksAsync(new ListTasksRequest
{
    ContextId = "my-context",
    PageSize = 10,
    Status = TaskState.Working,
});

// Delete push notification config
await client.DeleteTaskPushNotificationConfigAsync(
    new DeleteTaskPushNotificationConfigRequest { TaskId = "t1", Id = "config1" });

// Get extended agent card
var extCard = await client.GetExtendedAgentCardAsync(
    new GetExtendedAgentCardRequest());
```

---

## Server API

V1 introduces `ITaskManager` for server implementations. Use `TaskManager` as a base or implement the interface directly.

```csharp
// Register handlers
var taskManager = new TaskManager(store, logger);

taskManager.OnSendMessage = async (request, ct) =>
{
    // Process the message and return a response
    var task = await store.CreateTaskAsync(...);
    return new SendMessageResponse { Task = task };
};

taskManager.OnCancelTask = async (request, ct) =>
{
    var task = await store.GetTaskAsync(request.Id);
    await store.UpdateStatusAsync(task.Id, new TaskStatus { State = TaskState.Canceled });
    return task;
};

// Map endpoints
app.MapA2A(taskManager, "/agent");
app.MapWellKnownAgentCard(agentCard);

// Optional: also map REST API
app.MapHttpA2A(taskManager, agentCard);
```

---

## New V1 Features

### ListTasks

Full pagination, filtering, and sorting support:

```csharp
var result = await client.ListTasksAsync(new ListTasksRequest
{
    ContextId = "conversation-123",
    Status = TaskState.Working,
    PageSize = 10,
    PageToken = nextPageToken,
    HistoryLength = 5,
    IncludeArtifacts = true,
    StatusTimestampAfter = DateTimeOffset.UtcNow.AddHours(-1),
});

foreach (var task in result.Tasks)
{
    Console.WriteLine($"{task.Id}: {task.Status.State}");
}

// Pagination
if (!string.IsNullOrEmpty(result.NextPageToken))
{
    // Fetch next page...
}
```

### REST API

V1 adds an HTTP+JSON REST binding alongside JSON-RPC:

```csharp
// Server-side: map both bindings
app.MapA2A(taskManager, "/agent");        // JSON-RPC
app.MapHttpA2A(taskManager, agentCard);   // REST API

// REST endpoints:
// GET  /v1/card
// GET  /v1/tasks/{id}
// POST /v1/tasks/{id}:cancel
// GET  /v1/tasks
// POST /v1/message:send
// POST /v1/message:stream
// ... and more
```

### Version Negotiation

The JSON-RPC binding supports version negotiation via the `A2A-Version` header:

- Empty or missing → accepted (defaults to current)
- `"0.3"` → accepted
- `"1.0"` → accepted
- Any other value → `VersionNotSupported` error (-32009)

### ContentCase / PayloadCase Enums

All proto `oneof` types have computed case enums for safe switching:

- `Part.ContentCase` → `PartContentCase { None, Text, Raw, Url, Data }`
- `SendMessageResponse.PayloadCase` → `SendMessageResponseCase { None, Task, Message }`
- `StreamResponse.PayloadCase` → `StreamResponseCase { None, Task, Message, StatusUpdate, ArtifactUpdate }`
- `SecurityScheme.SchemeCase` → `SecuritySchemeCase { None, ApiKey, HttpAuth, OAuth2, OpenIdConnect, Mtls }`
- `OAuthFlows.FlowCase` → `OAuthFlowCase { None, AuthorizationCode, ClientCredentials, ... }`

### New Error Codes

| Code | Name | When |
|------|------|------|
| -32006 | `InvalidAgentResponse` | Internal agent response error |
| -32007 | `ExtendedAgentCardNotConfigured` | Extended card not available |
| -32008 | `ExtensionSupportRequired` | Extension not supported |
| -32009 | `VersionNotSupported` | Invalid A2A-Version header |

---

## Backward Compatibility

During migration, you can use the `A2A.V0_3` NuGet package for backward compatibility:

```csharp
// V0.3 namespace
using A2A.V0_3;

// V1 namespace
using A2A;

// Both can coexist — use fully qualified names when ambiguous
var v03Part = new A2A.V0_3.TextPart { Text = "hello" };
var v1Part = A2A.Part.FromText("hello");
```

The `A2A.V0_3` package is a standalone snapshot of the v0.3 SDK. It has no dependencies on the v1 package and can be used indefinitely during the transition period.
