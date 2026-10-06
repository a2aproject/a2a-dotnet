# Custom Operation Registry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a small, strongly typed custom-operation registry that exposes external unary and streaming commands through JSON-RPC, HTTP+JSON, generic gRPC, and dedicated protobuf services without copying A2A transport processors.

**Architecture:** Standard A2A operations retain their existing dispatch paths. A new immutable registry owns custom operation identity, JSON metadata, validation, and typed handler invocation; transport-specific binding tables adapt JSON-RPC methods, HTTP routes, and the generic gRPC envelope to registry entries. The Agents SDK InTaskAuthorization integration consumes only these public APIs and deletes its copied `A2AJsonRpcProcessor` and `A2AHttpProcessor`.

**Tech Stack:** C# 13, .NET 8/.NET 10, ASP.NET Core minimal APIs, System.Text.Json source-generated metadata, gRPC/protobuf, xUnit.

**Spec:** `D:\code\docs\a2a\Unified-operations-and-extension\A2A-dotnet-Simplified-Custom-Operation-Registry-Design.md`

## Global Constraints

- Preserve all existing standard JSON-RPC, HTTP+JSON, and gRPC endpoint signatures and wire behavior.
- Custom JSON-RPC lookup occurs only after standard method lookup and before `MethodNotFound`.
- Custom JSON-RPC method names cannot shadow standard A2A methods.
- Every custom operation registration requires closed generic request/result types and explicit `JsonTypeInfo<T>` metadata.
- Handlers remain transport-neutral and receive only the typed request and cancellation token.
- Expected failures use `A2AException`; unexpected failures are sanitized by each transport.
- Do not add assembly scanning, runtime generic construction, expression compilation, or reflection-based serializer lookup.
- Do not copy, subclass, or replace A2A transport processors in consuming integrations.

---

### Task 1: Typed Custom Operation Registry

**Files:**
- Create: `src/A2A/Server/A2AOperationId.cs`
- Create: `src/A2A/Server/A2ACustomOperation.cs`
- Create: `src/A2A/Server/A2ACustomOperationRegistryBuilder.cs`
- Create: `src/A2A/Server/A2ACustomOperationRegistry.cs`
- Create: `src/A2A/Diagnostics/A2ACustomOperationDiagnostics.cs`
- Create: `tests/A2A.UnitTests/Server/A2ACustomOperationRegistryTests.cs`

**Interfaces:**
- Produces: `A2AOperationId`, `A2ACustomOperation<TRequest,TResult>`, `A2AStreamingCustomOperation<TRequest,TEvent>`.
- Produces: `A2ACustomOperationRegistryBuilder.Map`, `MapStreaming`, and `Build`.
- Produces: `A2ACustomOperationRegistry.InvokeAsync` and `InvokeStreamingAsync`.
- Produces internal transport lookup entries carrying operation ID, kind, request/output `JsonTypeInfo`, and untyped invocation delegates.

- [ ] **Step 1: Write registry construction and invocation tests**

Cover unary and streaming invocation, validation-before-handler, cancellation, empty/duplicate IDs, null delegates/metadata, build immutability, foreign handles, and concurrent invocation. Use source-generated test metadata:

```csharp
[JsonSerializable(typeof(TestRequest))]
[JsonSerializable(typeof(TestResult))]
internal partial class CustomOperationJsonContext : JsonSerializerContext;
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```powershell
dotnet test tests\A2A.UnitTests\A2A.UnitTests.csproj --no-restore --filter FullyQualifiedName~A2ACustomOperationRegistryTests
```

Expected: compilation fails because the custom operation types do not exist.

- [ ] **Step 3: Implement operation identity, typed handles, and delegates**

Use these public signatures:

```csharp
public readonly record struct A2AOperationId(string Value);

public delegate ValueTask<TResult> A2ACustomOperationHandler<TRequest, TResult>(
    TRequest request,
    CancellationToken cancellationToken);

public delegate IAsyncEnumerable<TEvent> A2AStreamingCustomOperationHandler<TRequest, TEvent>(
    TRequest request,
    CancellationToken cancellationToken);

public delegate void A2ACustomOperationValidator<TRequest>(TRequest request);
```

Handle constructors remain internal and each handle carries a private registration token so another registry cannot accept it.

- [ ] **Step 4: Implement the builder and immutable registry**

The builder validates arguments immediately, rejects duplicate IDs across unary and streaming registrations, and refuses registration after `Build`. The registry stores frozen read-only dictionaries and invokes prebuilt typed delegates without reflection.

- [ ] **Step 5: Add common diagnostics**

Create an internal `ActivitySource` named `A2A.CustomOperations` and tag invocation activities with:

```text
a2a.operation.id
a2a.operation.kind
a2a.operation.source=custom
a2a.operation.role=server
a2a.operation.outcome
```

Do not record request, response, event, metadata, or authorization values.

- [ ] **Step 6: Run registry tests**

Run the focused command from Step 2 and expect all tests to pass for `net8.0` and `net10.0`.

- [ ] **Step 7: Commit**

```powershell
git add src\A2A\Server src\A2A\Diagnostics\A2ACustomOperationDiagnostics.cs tests\A2A.UnitTests\Server\A2ACustomOperationRegistryTests.cs
git commit -m "Add typed custom operation registry" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: de52387f-5762-4265-910b-6b3370f523b1"
```

### Task 2: JSON-RPC Custom Operation Binding

**Files:**
- Create: `src/A2A.AspNetCore/A2AJsonRpcCustomOperationBindings.cs`
- Modify: `src/A2A.AspNetCore/A2AJsonRpcProcessor.cs`
- Modify: `src/A2A.AspNetCore/A2AEndpointRouteBuilderExtensions.cs`
- Create: `src/A2A.AspNetCore/CustomJsonRpcStreamedResult.cs`
- Create: `tests/A2A.AspNetCore.UnitTests/A2AJsonRpcCustomOperationTests.cs`

**Interfaces:**
- Consumes: registry lookup entries and typed handles from Task 1.
- Produces: `A2AJsonRpcCustomOperationBuilder.Map`, `MapStreaming`, and `Build`.
- Produces: additive `MapA2A(requestHandler, path, registry, customBindings)` overload.

- [ ] **Step 1: Write JSON-RPC custom operation tests**

Cover registered unary success, registered streaming SSE, malformed custom parameters, handler `A2AException`, sanitized unexpected exception, unknown method, request ID preservation, standard method precedence, duplicate method rejection, standard-name rejection, foreign-registry binding rejection, and unchanged standard-only overload behavior.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test tests\A2A.AspNetCore.UnitTests\A2A.AspNetCore.UnitTests.csproj --no-restore --filter FullyQualifiedName~A2AJsonRpcCustomOperationTests
```

Expected: compilation fails because JSON-RPC custom bindings and endpoint overloads do not exist.

- [ ] **Step 3: Implement immutable JSON-RPC bindings**

Use these public APIs:

```csharp
public sealed class A2AJsonRpcCustomOperationBuilder
{
    public A2AJsonRpcCustomOperationBuilder Map<TRequest, TResult>(
        string method,
        A2ACustomOperation<TRequest, TResult> operation);

    public A2AJsonRpcCustomOperationBuilder MapStreaming<TRequest, TEvent>(
        string method,
        A2AStreamingCustomOperation<TRequest, TEvent> operation);

    public A2AJsonRpcCustomOperationBindings Build(A2ACustomOperationRegistry registry);
}
```

Reject empty methods, duplicates, and every method recognized by `A2AMethods`.

- [ ] **Step 4: Add processor fallback**

Pass optional registry/bindings into `ProcessRequestAsync`, `SingleResponseAsync`, and `StreamResponse`. Preserve the existing switch for standard methods; only each switch `default` consults the custom bindings before returning `MethodNotFound`.

For unary operations, deserialize `params` with the registration’s request `JsonTypeInfo`, invoke the registry, and create the normal `JsonRpcResponse`. Translate malformed input to `A2AErrorCode.InvalidParams`.

- [ ] **Step 5: Add custom JSON-RPC streaming result**

Create an `IResult` equivalent to `JsonRpcStreamedResult`, but serialize each custom event with the registration’s event `JsonTypeInfo` and wrap it in a JSON-RPC result envelope preserving the request ID. Preserve current pre-start/post-start exception sanitization and cancellation behavior.

- [ ] **Step 6: Add the public endpoint overload**

Add:

```csharp
public static IEndpointConventionBuilder MapA2A(
    this IEndpointRouteBuilder endpoints,
    IA2ARequestHandler requestHandler,
    string path,
    A2ACustomOperationRegistry registry,
    A2AJsonRpcCustomOperationBindings customBindings);
```

Keep both existing overloads unchanged.

- [ ] **Step 7: Run JSON-RPC and baseline processor tests**

```powershell
dotnet test tests\A2A.AspNetCore.UnitTests\A2A.AspNetCore.UnitTests.csproj --no-restore --filter "FullyQualifiedName~A2AJsonRpcCustomOperationTests|FullyQualifiedName~A2AJsonRpcProcessorTests|FullyQualifiedName~JsonRpcStreamedResultTests"
```

Expected: all selected tests pass on both target frameworks.

- [ ] **Step 8: Commit**

```powershell
git add src\A2A.AspNetCore tests\A2A.AspNetCore.UnitTests
git commit -m "Add JSON-RPC custom operation dispatch" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: de52387f-5762-4265-910b-6b3370f523b1"
```

### Task 3: HTTP+JSON Custom Routes

**Files:**
- Create: `src/A2A.AspNetCore/A2AHttpCustomOperationBindings.cs`
- Create: `src/A2A.AspNetCore/CustomHttpOperationResults.cs`
- Modify: `src/A2A.AspNetCore/A2AEndpointRouteBuilderExtensions.cs`
- Create: `tests/A2A.AspNetCore.UnitTests/A2AHttpCustomOperationTests.cs`

**Interfaces:**
- Consumes: Task 1 registry and handles.
- Produces: typed `A2AHttpCustomRequestBinder<TRequest>`.
- Produces: `A2AHttpCustomOperationBuilder.Map`, `MapStreaming`, and `Build`.
- Produces: additive `MapHttpA2A(requestHandler, path, registry, customBindings)` overload.

- [ ] **Step 1: Write HTTP custom route tests**

Cover unary JSON response, streaming SSE, binder access to route/query/header/body, semantic validation, `A2AException` HTTP mapping, sanitized unexpected exceptions, duplicate method/route rejection, foreign registry rejection, cancellation, and unchanged standard routes.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test tests\A2A.AspNetCore.UnitTests\A2A.AspNetCore.UnitTests.csproj --no-restore --filter FullyQualifiedName~A2AHttpCustomOperationTests
```

- [ ] **Step 3: Implement typed HTTP bindings**

Use:

```csharp
public delegate ValueTask<TRequest> A2AHttpCustomRequestBinder<TRequest>(
    HttpContext httpContext,
    CancellationToken cancellationToken);
```

Store verb, route, registry entry, and binder in immutable bindings. Reject empty methods/routes and duplicate case-insensitive verb plus route pairs.

- [ ] **Step 4: Implement transport-owned result writers**

Unary results serialize with the operation’s result `JsonTypeInfo` and `application/json`. Streaming results write `text/event-stream` events using the operation’s event `JsonTypeInfo`. Before response start, map `A2AException` through `A2AErrorResult`; sanitize unexpected failures. After response start, do not leak exception details.

- [ ] **Step 5: Map custom routes through the public endpoint overload**

Map standard routes exactly as today, then map each custom binding under the same route group. The endpoint adapter calls the binding’s typed binder and registry entry; consuming code never calls or copies `A2AHttpProcessor`.

- [ ] **Step 6: Run HTTP and endpoint baseline tests**

```powershell
dotnet test tests\A2A.AspNetCore.UnitTests\A2A.AspNetCore.UnitTests.csproj --no-restore --filter "FullyQualifiedName~A2AHttpCustomOperationTests|FullyQualifiedName~A2AEndpointRouteBuilderExtensionsTests"
```

- [ ] **Step 7: Commit**

```powershell
git add src\A2A.AspNetCore tests\A2A.AspNetCore.UnitTests
git commit -m "Add HTTP custom operation routes" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: de52387f-5762-4265-910b-6b3370f523b1"
```

### Task 4: Generic gRPC Custom Operation Envelope

**Files:**
- Create: `src/A2A.Grpc/Protos/a2a_extensions.proto`
- Modify: `src/A2A.Grpc/A2A.Grpc.csproj`
- Create: `src/A2A.Grpc.AspNetCore/A2AGrpcCustomOperationService.cs`
- Modify: `src/A2A.Grpc.AspNetCore/GrpcA2ARouteBuilderExtensions.cs`
- Create: `tests/A2A.Grpc.UnitTests/GrpcCustomOperationTests.cs`

**Interfaces:**
- Consumes: registry transport lookup and JSON metadata from Task 1.
- Produces protobuf `A2AExtensionService` with unary and server-streaming envelope methods.
- Produces `AddA2AGrpcCustomOperations(registry)` and `MapGrpcA2ACustomOperations()`.

- [ ] **Step 1: Write generic gRPC integration tests**

Cover unary and streaming dispatch, unknown ID, unary/streaming kind mismatch, malformed JSON payload, registered JSON metadata, `A2AException`, sanitized unexpected exception, and cancellation.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test tests\A2A.Grpc.UnitTests\A2A.Grpc.UnitTests.csproj --no-restore --filter FullyQualifiedName~GrpcCustomOperationTests
```

- [ ] **Step 3: Add the extension envelope protobuf contract**

Define:

```protobuf
service A2AExtensionService {
  rpc InvokeExtensionOperation(ExtensionOperationRequest)
      returns (ExtensionOperationResponse);
  rpc InvokeStreamingExtensionOperation(ExtensionOperationRequest)
      returns (stream ExtensionOperationEvent);
}

message ExtensionOperationRequest {
  string operation_id = 1;
  bytes payload = 2;
}

message ExtensionOperationResponse { bytes payload = 1; }
message ExtensionOperationEvent { bytes payload = 1; }
```

- [ ] **Step 4: Implement the generic gRPC service**

Resolve `A2AOperationId`, enforce operation kind, deserialize UTF-8 JSON with the registered request metadata, invoke the registry, and serialize results/events with registered output metadata. Map expected errors through `GrpcErrorMapping`; use a generic internal `RpcException` for unexpected failures.

- [ ] **Step 5: Add service registration and mapping**

`AddA2AGrpcCustomOperations` registers the exact immutable registry singleton and calls `AddGrpc`. `MapGrpcA2ACustomOperations` maps only the envelope service. Existing `AddA2AGrpc` and `MapGrpcA2A` remain unchanged.

- [ ] **Step 6: Run custom and standard gRPC tests**

```powershell
dotnet test tests\A2A.Grpc.UnitTests\A2A.Grpc.UnitTests.csproj --no-restore --filter "FullyQualifiedName~GrpcCustomOperationTests|FullyQualifiedName~GrpcIntegrationTests"
```

- [ ] **Step 7: Commit**

```powershell
git add src\A2A.Grpc src\A2A.Grpc.AspNetCore tests\A2A.Grpc.UnitTests
git commit -m "Add gRPC custom operation envelope" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: de52387f-5762-4265-910b-6b3370f523b1"
```

### Task 5: Dedicated Protobuf and Native AOT Coverage

**Files:**
- Create: `tests/A2A.Grpc.UnitTests/Protos/test_custom_operations.proto`
- Modify: `tests/A2A.Grpc.UnitTests/A2A.Grpc.UnitTests.csproj`
- Create: `tests/A2A.Grpc.UnitTests/DedicatedGrpcCustomOperationTests.cs`
- Modify: `tests/A2A.AotTests/Program.cs`
- Modify: `tests/A2A.AotTests/A2A.AotTests.csproj`

**Interfaces:**
- Consumes: public typed registry invocation from Task 1.
- Demonstrates dedicated generated protobuf services invoke exact startup-created handles.
- Demonstrates JSON-RPC, HTTP, generic gRPC, and dedicated protobuf registrations remain source-generation/AOT compatible.

- [ ] **Step 1: Add a test-only dedicated protobuf service**

Define one unary and one server-streaming RPC. Implement the generated service by converting protobuf messages to test domain requests, invoking `registry.InvokeAsync`/`InvokeStreamingAsync`, and converting domain outputs back to protobuf.

- [ ] **Step 2: Add dedicated service integration tests**

Assert both RPCs reach the same handlers used by registry tests and that validation/cancellation propagate.

- [ ] **Step 3: Extend the AOT smoke application**

Register one unary and one streaming operation with a source-generated `JsonSerializerContext`, create JSON-RPC and HTTP bindings, and register the generic gRPC envelope without reflection warnings.

- [ ] **Step 4: Run focused verification**

```powershell
dotnet test tests\A2A.Grpc.UnitTests\A2A.Grpc.UnitTests.csproj --no-restore --filter FullyQualifiedName~DedicatedGrpcCustomOperationTests
dotnet publish tests\A2A.AotTests\A2A.AotTests.csproj -c Release -f net8.0
```

- [ ] **Step 5: Commit**

```powershell
git add tests\A2A.Grpc.UnitTests tests\A2A.AotTests
git commit -m "Verify dedicated gRPC and AOT custom operations" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: de52387f-5762-4265-910b-6b3370f523b1"
```

### Task 6: Agents SDK InTaskAuthorization Consumer

**Files:**
- Modify in `D:\code\Agents-for-net`: `src/libraries/Extensions/Microsoft.Agents.Extensions.A2A/ProtocolExtensions/InTaskAuthorization/*`
- Modify in `D:\code\Agents-for-net`: `src/libraries/Extensions/Microsoft.Agents.Extensions.A2A/Pipeline/A2AAdapter.cs`
- Modify in `D:\code\Agents-for-net`: `src/libraries/Extensions/Microsoft.Agents.Extensions.A2A/A2AServiceExtensions.cs`
- Delete in `D:\code\Agents-for-net`: `src/libraries/Extensions/Microsoft.Agents.Extensions.A2A/Pipeline/A2AJsonRpcProcessor.cs`
- Delete in `D:\code\Agents-for-net`: `src/libraries/Extensions/Microsoft.Agents.Extensions.A2A/Pipeline/A2AHttpProcessor.cs`
- Modify in `D:\code\Agents-for-net`: `src/tests/Microsoft.Agents.Extensions.A2A.Tests/*`

**Interfaces:**
- Consumes only public A2A custom registry and transport binding APIs.
- Produces InTaskAuthorization unary/streaming registrations and package endpoint wiring.
- Removes package-owned transport processor copies.

- [ ] **Step 1: Create a new Agents SDK comparison branch**

```powershell
git -C D:\code\Agents-for-net fetch origin --prune
git -C D:\code\Agents-for-net switch -c users/tracyboehrer/a2a-custom-operation-registry origin/main
```

- [ ] **Step 2: Reference locally built A2A packages or projects**

Update the extension test/build path to consume the comparison branch artifacts without changing unrelated package consumers. Keep the dependency change isolated so it can later be replaced by published package versions.

- [ ] **Step 3: Port only InTaskAuthorization behavior from the POC**

Use `users/tracyboehrer/a2a-operations-poc` as behavioral reference for operation IDs, JSON-RPC methods, HTTP routes, models, validation, and handler behavior. Register those operations through `A2ACustomOperationRegistryBuilder`, then create JSON-RPC and HTTP custom bindings.

- [ ] **Step 4: Replace adapter processor calls with upstream endpoint APIs**

Wire the package’s agent endpoint through the new `MapA2A` and `MapHttpA2A` overloads. Do not parse JSON-RPC envelopes or write A2A HTTP responses in the package.

- [ ] **Step 5: Delete both copied processors**

Delete the package-owned `A2AJsonRpcProcessor.cs` and `A2AHttpProcessor.cs`, then search for remaining declarations or references:

```powershell
rg "A2AJsonRpcProcessor|A2AHttpProcessor" D:\code\Agents-for-net\src\libraries\Extensions\Microsoft.Agents.Extensions.A2A
```

Expected: no package-owned processor declaration or call remains.

- [ ] **Step 6: Add integration tests**

Verify standard A2A requests still work, InTaskAuthorization unary and streaming behavior works through upstream JSON-RPC and HTTP infrastructure, errors retain request IDs/status mapping, and no copied processors are required.

- [ ] **Step 7: Run Agents SDK tests**

```powershell
dotnet test D:\code\Agents-for-net\src\tests\Microsoft.Agents.Extensions.A2A.Tests\Microsoft.Agents.Extensions.A2A.Tests.csproj
dotnet test D:\code\Agents-for-net\src\tests\Microsoft.Agents.Samples.A2A.Tests\Microsoft.Agents.Samples.A2A.Tests.csproj
```

- [ ] **Step 8: Commit**

```powershell
git -C D:\code\Agents-for-net add src\libraries\Extensions\Microsoft.Agents.Extensions.A2A src\tests\Microsoft.Agents.Extensions.A2A.Tests src\tests\Microsoft.Agents.Samples.A2A.Tests
git -C D:\code\Agents-for-net commit -m "Use upstream A2A custom operation registry" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: de52387f-5762-4265-910b-6b3370f523b1"
```

### Task 7: Full Compatibility Verification

**Files:**
- Modify only files required to fix defects introduced by Tasks 1-6.

**Interfaces:**
- Verifies the public API and all consumer behavior; produces no new architecture.

- [ ] **Step 1: Run the complete a2a-dotnet test suite**

```powershell
dotnet test A2A.slnx --no-restore
```

- [ ] **Step 2: Build all package projects**

```powershell
dotnet build A2A.slnx --no-restore
```

- [ ] **Step 3: Verify branch diffs**

```powershell
git status --short
git diff --check origin/main...HEAD
git --no-pager diff --stat origin/main...HEAD
git -C D:\code\Agents-for-net status --short
git -C D:\code\Agents-for-net diff --check origin/main...HEAD
```

- [ ] **Step 4: Verify the processor-copy acceptance criterion**

Confirm the Agents SDK branch contains InTaskAuthorization behavior, contains no package-owned `A2AJsonRpcProcessor` or `A2AHttpProcessor`, and passes its focused extension/sample tests against the new a2a-dotnet artifacts.

- [ ] **Step 5: Commit any verification fixes**

Use a narrowly scoped commit message describing the actual correction and include the required Copilot trailers.
