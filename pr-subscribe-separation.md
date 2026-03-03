# A2A .NET SDK v1 Implementation

## Summary

Complete implementation of the A2A v1 specification for the .NET SDK, including protocol models, server-side architecture redesign, simple CRUD task store, atomic subscribe, observability, and backward compatibility with v0.3.

**239 files changed, 19,753 insertions, 8,769 deletions** across 14 commits.

## How to Review

This PR is large but logically layered. Recommended review order by commit:

| # | Commit | Focus Area | Key Files |
|---|--------|-----------|-----------|
| 1 | `d223872` | **v1 spec models + SDK refactor** | `src/A2A/Models/`, client, v0.3 compat |
| 2 | `3ba763c` | Review findings fixes | Tests, docs, cleanup |
| 3 | `9e11c78` | **Security hardening** | `A2AHttpProcessor.cs`, `A2AJsonRpcProcessor.cs` |
| 4 | `0ebe07a` | **TFM update** (net10.0+net8.0) | `.csproj` files, removed polyfills |
| 5 | `ad6e513` | **Server architecture redesign** | `A2AServer.cs`, `IAgentHandler.cs`, DI, observability |
| 6 | `2f8e41f` | REST handler tracing | AspNetCore processors |
| 7 | `9979994` | Stale task fix | `MaterializeResponseAsync`, `CancelTaskAsync` |
| 8 | `03328b2` | Event sourcing (intermediate) | `IEventStore.cs`, `InMemoryEventStore.cs` |
| 9 | `bb859a1` | FileEventStore sample (intermediate) | `samples/AgentServer/FileEventStore.cs` |
| 10 | `31a57ba` | FileStoreDemo sample | `samples/FileStoreDemo/` |
| 11 | `6cc23bb` | Subscribe separation (intermediate) | `IEventSubscriber.cs`, `ChannelEventNotifier.cs` |
| 12 | `a7df8a6` | **Replace ES with CRUD ITaskStore** | `ITaskStore.cs`, `InMemoryTaskStore.cs`, atomic subscribe |
| 13 | `4cf8a59`+`f0bd87e` | **Cleanup** | `TaskCreatedCount` fix, `ApplyEventAsync` rename |
| 14 | `752caf0` | **CancelTask metadata** (spec late change) | `CancelTaskRequest.cs` |

> **Note:** Commits 8-11 introduce event sourcing then commit 12 replaces it with a simpler CRUD store. This was an intentional design evolution — the event-sourcing approach was analyzed against the spec, found to be unnecessary burden for store implementors, and replaced with a 4-method CRUD interface + atomic per-task locking. See the [design discussion](#8-store-architecture-evolution) below.

## Major Changes

### 1. A2A v1 Spec Models (`d223872`)

- Fix 16 proto fidelity gaps across 46 model types
- Add `ContentCase`/`PayloadCase` computed enums for all `oneof` types
- Implement full `ListTasks` with pagination, sorting, filtering
- Port REST API (`MapHttpA2A`) with 11 endpoints and SSE streaming
- Add `A2A-Version` header negotiation to JSON-RPC processor
- Create `A2A.V0_3` backward compatibility project (65 files, 255 tests)
- Add `docs/migration-guide-v1.md` (12 sections, 21 breaking changes documented)

### 2. Security Hardening (`9e11c78`)

- Sanitize raw `Exception.Message` in 5 HTTP/JSON-RPC error responses to prevent leaking internal details (stack traces, file paths, DB errors)
- Sanitize `JsonException.Message` in `DeserializeAndValidate` to avoid exposing internal type/path details
- `A2AException.Message` still exposed (intentional, app-defined errors)
- Add error handling to SSE streaming (best-effort JSON-RPC error event on failure)

### 3. Target Framework Update (`0ebe07a`)

- `net9.0;net8.0;netstandard2.0` → `net10.0;net8.0` across all projects
- Conditionally exclude `System.Linq.AsyncEnumerable` and `System.Net.ServerSentEvents` for net10.0 (now in-box)
- Remove 8 polyfill files and all `#if NET` conditional compilation blocks

### 4. Server Architecture Redesign (`ad6e513`)

**BREAKING**: `ITaskManager`/`TaskManager` → `IA2ARequestHandler`/`A2AServer`

- **`A2AServer`**: Lifecycle orchestrator with context resolution, terminal state guards, event persistence, history management, and try/finally safety net preventing deadlocks
- **`IAgentHandler`**: Easy-path agent contract (`ExecuteAsync` + default `CancelAsync`)
- **`AgentEventQueue`**: Channel-backed event stream with typed `Enqueue*` methods
- **`TaskUpdater`**: Lifecycle convenience helper for all 8 task states
- **`A2ADiagnostics`**: `ActivitySource('A2A')` + `Meter('A2A')` with 9 metric instruments
- **DI**: `AddA2AAgent<T>()` one-line registration + `MapA2A(path)` endpoint mapping
- All sample agents rewritten: 67-144 lines → 20-97 lines

### 5. Simple CRUD Task Store (`a7df8a6`)

Replace event sourcing with a 4-method `ITaskStore` interface matching the Python SDK pattern:

```csharp
public interface ITaskStore
{
    Task<AgentTask?> GetTaskAsync(string taskId, ...);
    Task SaveTaskAsync(string taskId, AgentTask task, ...);
    Task DeleteTaskAsync(string taskId, ...);
    Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, ...);
}
```

- **`InMemoryTaskStore`**: `ConcurrentDictionary<string, AgentTask>` with deep clone
- **`FileTaskStore`** (sample): File-backed store with task JSON files + context indexes
- **`TaskProjection.Apply`**: Used by `A2AServer` to mutate task state before saving
- **Atomic subscribe**: Per-task `SemaphoreSlim` in `ChannelEventNotifier` guarantees no events missed between task snapshot and channel registration

### 6. CancelTask Metadata (`752caf0`)

- Add `Metadata` property to `CancelTaskRequest` (spec proto field 3, late addition)
- Wire `request.Metadata` into `AgentContext` in `CancelTaskAsync`

### 7. FileStoreDemo Sample (`31a57ba`)

- Self-contained demo showing data recovery after server restart
- Demonstrates `ListTasksAsync` by context filter across restarts

## 8. Store Architecture Evolution

The PR went through an intentional design evolution:

1. **Event sourcing** (commits 8-11): Introduced `IEventStore`/`ITaskEventStore` with 7 methods, versioned event logs, `ChannelEventSubscriber` with catch-up replay
2. **Analysis**: Studied the A2A spec — it requires mutable task state, not event logs. The Python SDK uses 3 methods. Event sourcing imposed unnecessary complexity on every custom store implementor.
3. **CRUD store** (commit 12): Replaced with `ITaskStore` (4 methods). Subscribe race condition solved via atomic per-task locking instead of store-level version tracking. Net result: -422 lines.

## Bug Fixes

- **Stale task objects** (`9979994`): Re-fetch task from store after consuming all events
- **TaskCreatedCount** (`4cf8a59`): Only counts when a genuinely new task is created (store has no existing task)
- **AutoPersistEvents removed** (`f0bd87e`): Was a footgun — server doesn't function without applying events. Renamed `PersistEventAsync` → `ApplyEventAsync`
- **Bounded channel deadlock** (`ad6e513`): Run handler concurrently with consumer

## Breaking Changes

| Change | Commit | Mitigation |
|--------|--------|------------|
| `ITaskManager` → `IA2ARequestHandler` | `ad6e513` | Rename + update constructor |
| `TaskManager` → `A2AServer` | `ad6e513` | Use `AddA2AAgent<T>()` DI helper |
| `ITaskStore` (new interface) | `a7df8a6` | 4 simple CRUD methods |
| TFM: netstandard2.0/net9.0 dropped | `0ebe07a` | Target net8.0 or net10.0 |
| `A2AServer` constructor requires `ChannelEventNotifier` | `a7df8a6` | Use DI (automatic) |
| `AutoPersistEvents` removed | `f0bd87e` | No longer configurable |

## Validation

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors, 0 warnings |
| `dotnet test` (net8.0 + net10.0) | 1,168 tests pass, 0 failures |
| TCK mandatory tests | 76/76 pass |
| FileStoreDemo data recovery | Works correctly |
| v0.3 backward compatibility | 262 tests pass per TFM |
