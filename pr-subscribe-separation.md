# A2A .NET SDK v1 Implementation

## Summary

Complete implementation of the A2A v1 specification for the .NET SDK, including protocol models, server-side architecture redesign, event sourcing, observability, and backward compatibility with v0.3.

**244 files changed, 20,144 insertions, 8,767 deletions** across 11 commits.

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
| 8 | `03328b2` | **Event sourcing** | `IEventStore.cs`, `InMemoryEventStore.cs`, `TaskProjection.cs` |
| 9 | `bb859a1` | FileEventStore sample | `samples/AgentServer/FileEventStore.cs` |
| 10 | `31a57ba` | FileStoreDemo sample | `samples/FileStoreDemo/` |
| 11 | `6cc23bb` | **Subscribe separation** | `IEventSubscriber.cs`, `ChannelEventNotifier.cs` |

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

### 5. Event Sourcing (`03328b2`)

Replace mutable `ITaskStore` with append-only event-sourced `ITaskEventStore`:

- **`IEventStore`**: `AppendAsync`, `ReadAsync`, `ExistsAsync`, `GetLatestVersionAsync`
- **`ITaskEventStore`**: Adds `GetTaskAsync`, `ListTasksAsync`, `GetTaskWithVersionAsync`
- **`TaskProjection`**: Pure function to materialize `AgentTask` from `StreamResponse` events
- **`InMemoryEventStore`**: Inline projection cache, per-task locking, deep cloning
- Implement spec-compliant `SubscribeToTaskAsync` with catch-up-then-live pattern
- Fix artifact append/replace semantics by `artifactId`
- Remove `ITaskStore`, `InMemoryTaskStore`, `DistributedCacheTaskStore`, `TaskStoreAdapter`

### 6. Subscribe Separation (`6cc23bb`)

Extract `SubscribeAsync` from `IEventStore` into dedicated `IEventSubscriber` interface:

```
ChannelEventNotifier (singleton)
     ↑ Notify()              ↑ CreateChannel()
     │                       │
InMemoryEventStore     ChannelEventSubscriber
(IEventStore)          (IEventSubscriber)
     ↑ AppendAsync()         ↑ SubscribeAsync()
     │                       │
     └───── A2AServer ───────┘
```

- Eliminates ~115 lines of duplicate subscriber boilerplate from store implementations
- Fixes TOCTOU race in `SubscribeToTaskAsync` via atomic `GetTaskWithVersionAsync`
- Custom store implementors no longer need to build pub/sub infrastructure

### 7. FileEventStore Sample (`bb859a1`, `31a57ba`)

- File-backed `ITaskEventStore` with per-task JSONL event logs
- Materialized projection files for O(1) `GetTaskAsync`
- Context index files for efficient `ListTasksAsync` filtering
- `FileStoreDemo`: Self-contained demo showing data recovery after server restart

## Bug Fixes

- **Stale task objects** (`9979994`): Re-fetch task from store after consuming all events instead of returning pre-mutation snapshot
- **TOCTOU race in subscribe** (`6cc23bb`): Atomic `GetTaskWithVersionAsync` prevents missed events or replayed non-idempotent artifact appends
- **Bounded channel deadlock** (`ad6e513`): Run handler concurrently with consumer
- **InMemoryTaskStore race conditions** (`d223872`): Fixed in v1 port, replaced entirely with event sourcing in `03328b2`

## Breaking Changes

| Change | Commit | Mitigation |
|--------|--------|------------|
| `ITaskManager` → `IA2ARequestHandler` | `ad6e513` | Rename + update constructor |
| `TaskManager` → `A2AServer` | `ad6e513` | Use `AddA2AAgent<T>()` DI helper |
| `ITaskStore` removed | `03328b2` | Implement `ITaskEventStore` instead |
| `IEventStore.SubscribeAsync` removed | `6cc23bb` | Subscribe logic now in `IEventSubscriber` |
| TFM: netstandard2.0/net9.0 dropped | `0ebe07a` | Target net8.0 or net10.0 |
| `InMemoryEventStore` requires `ChannelEventNotifier` | `6cc23bb` | Use DI (automatic) |
| `A2AServer` requires `IEventSubscriber` | `6cc23bb` | Use DI (automatic) |

## Validation

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors, 0 warnings |
| `dotnet test` (net8.0 + net10.0) | 906 tests pass, 0 failures |
| TCK mandatory tests | 76/76 pass |
| FileStoreDemo data recovery | Works correctly |
| v0.3 backward compatibility | 262 tests pass per TFM |
