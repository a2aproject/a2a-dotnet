# Developer Guide: Adding and Modifying A2A Operations

This guide explains the "operations catalog" architecture used by the A2A
.NET SDK and walks through the concrete steps required to add a new
operation, or modify an existing one. It is intended for engineers (human or
AI) who are not yet familiar with this part of the codebase.

## Why an operations catalog?

A2A exposes the same logical operations (`send-message`, `get-task`, etc.)
over multiple transports: JSON-RPC and HTTP+JSON REST. Rather than
duplicating request validation, dispatch, and error handling per transport,
the SDK defines each operation **once**, in a transport-neutral way, and then
separately **binds** that definition to each transport.

> **Note on gRPC:** the `A2A.Grpc`/`A2A.Grpc.AspNetCore` packages
> intentionally do **not** use this catalog. The gRPC contract is a fixed,
> code-generated surface (from the vendored `.proto`), so `A2AGrpcService`
> binds directly to `IA2ARequestHandler` with one method per RPC, and gets
> per-method dispatch, routing, and tracing for free from the gRPC/ASP.NET
> Core stack. The catalog exists to solve a problem gRPC doesn't have:
> multiplexing many dynamically-registered operations (including custom
> extensions) behind a single JSON-RPC/HTTP endpoint.

This separation is implemented with three cooperating catalogs:

| Catalog | Type | Purpose |
|---|---|---|
| Operation catalog | `A2AOperationCatalog` (built by `A2AOperationCatalogBuilder`) | Declares *what* an operation is: its id, request/result types, unary vs. streaming, semantic validator, and declared errors. |
| Handler catalog | `A2AOperationHandlerCatalog` (built by `A2AOperationHandlerCatalogBuilder`) | Maps each declared operation to the code that actually executes it (typically delegating to `IA2ARequestHandler`). |
| Transport bindings | `A2AJsonRpcOperationBindings` / `A2AHttpOperationBindings` (built by `A2AJsonRpcOperationBindingBuilder` / `A2AHttpOperationBindingBuilder`) | Maps each operation to a JSON-RPC method name or an HTTP route + verb, including request binding/deserialization and response serialization. |

Key source files:

- `src/A2A/Operations/A2AOperationCatalog.cs` — operation definitions
  (`A2AOperationCatalogBuilder`, `A2AOperationCatalog`, `A2AOperation<,>`,
  `A2AStreamingOperation<,>`).
- `src/A2A/Operations/A2AOperationHandlerCatalog.cs` — handler registration
  and invocation (`A2AOperationHandlerCatalogBuilder`,
  `A2AOperationHandlerCatalog`).
- `src/A2A/Operations/A2AStandardOperations.cs` — the built-in operation
  *definitions* (`A2AStandardOperations`,
  `AddStandardA2AOperations`).
- `src/A2A/Operations/A2AStandardOperationHandlers.cs` — the built-in
  operation *handlers* (`AddStandardA2AHandlers`), which forward to
  `IA2ARequestHandler`.
- `src/A2A.AspNetCore/A2AJsonRpcOperationBindings.cs` +
  `A2AStandardJsonRpcBindings.cs` — JSON-RPC method bindings.
- `src/A2A.AspNetCore/A2AHttpOperationBindings.cs` +
  `A2AStandardHttpBindings.cs` — HTTP+JSON route bindings.
- `src/A2A.AspNetCore/A2AEndpointRouteBuilderExtensions.cs` — wires the
  catalogs together and maps ASP.NET Core endpoints (`MapA2A`,
  `MapHttpA2A`).

## The pieces of an operation

Every operation is identified by a transport-neutral `A2AOperationId`
(a URI-like string, e.g. `https://a2a-protocol.org/operations/get-task`) and
is one of two kinds:

- **Unary** (`A2AOperation<TRequest, TResult>`) — one request, one result.
- **Streaming** (`A2AStreamingOperation<TRequest, TEvent>`) — one request,
  a stream of events (`IAsyncEnumerable<TEvent>`).

Both are opaque, reference-equality-checked *handles* returned by the
operation catalog builder. The handle is what you pass around — to
register a handler, to bind a transport route, or to declare an error — so
the catalogs can validate that everything is wired to the same logically
registered operation.

## End-to-end flow (standard operations)

The diagram below shows how a standard operation (e.g. `get-task`) is
assembled at startup and then dispatched at request time over HTTP+JSON.
JSON-RPC follows the same shape with `A2AJsonRpcOperationBindings` instead
of `A2AHttpOperationBindings`.

```mermaid
sequenceDiagram
    participant App as Host app (Program.cs)
    participant OCB as A2AOperationCatalogBuilder
    participant HCB as A2AOperationHandlerCatalogBuilder
    participant BB as A2AHttpOperationBindingBuilder
    participant Endpoints as ASP.NET Core routing

    App->>OCB: AddStandardA2AOperations()
    OCB-->>App: A2AStandardOperations (typed handles)
    App->>OCB: Build()
    OCB-->>App: A2AOperationCatalog

    App->>HCB: AddStandardA2AHandlers(standard)
    Note right of HCB: Map(standard.GetTask, handler lambda)
    App->>HCB: Build(operationCatalog)
    Note right of HCB: Validates every handler's id/kind/types<br/>exist in the operation catalog
    HCB-->>App: A2AOperationHandlerCatalog

    App->>BB: AddStandardA2AHttpBindings(standard)
    Note right of BB: Map(GET, "/tasks/{id}", standard.GetTask, binder, typeInfo)
    App->>BB: Build()
    BB-->>App: A2AHttpOperationBindings

    App->>Endpoints: MapHttpA2A(scopeFactory, handlers, bindings, path)
    Note right of Endpoints: bindings.MapEndpoints(...) registers<br/>one ASP.NET Core route per binding
```

At request time:

```mermaid
sequenceDiagram
    participant Client
    participant Route as ASP.NET Core route (e.g. GET /tasks/{id})
    participant Binding as HTTP binding (request binder)
    participant Scope as A2ARequestScopeFactory
    participant Handlers as A2AOperationHandlerCatalog
    participant Handler as Registered handler delegate
    participant RH as IA2ARequestHandler

    Client->>Route: HTTP GET /tasks/123
    Route->>Binding: requestBinder(httpContext, ct)
    Binding-->>Route: GetTaskRequest { Id = "123" }
    Route->>Scope: scopeFactory(...)
    Scope-->>Route: A2ARequestScope (A2AOperationContext)
    Route->>Handlers: InvokeAsync(standard.GetTask, context, request, ct)
    Handlers->>Handler: handler(context, request, ct)
    Handler->>RH: RequestHandler.GetTaskAsync(request, ct)
    RH-->>Handler: AgentTask
    Handler-->>Handlers: AgentTask
    Handlers-->>Route: AgentTask
    Route-->>Client: 200 OK (serialized AgentTask)
```

## Adding a brand-new operation

Use this checklist when the A2A protocol (or an extension) introduces a
genuinely new operation.

1. **Define the request/result (or event) types**, if they don't already
   exist, as ordinary DTOs under `src/A2A` (following existing patterns,
   e.g. `GetTaskRequest`, `AgentTask`).

2. **Declare the operation** in `A2AStandardOperations.cs` (for protocol
   standard operations) or in your own extension class (for custom/
   non-standard operations):
   - Add a field/property of type `A2AOperation<TRequest, TResult>` or
     `A2AStreamingOperation<TRequest, TEvent>` to the holder type
     (`A2AStandardOperations` or your own).
   - In the builder extension method, call
     `builder.DefineStandardUnary<TRequest, TResult>(new A2AOperationId("..."), validator)`
     (standard operations) or `builder.DefineUnary<TRequest, TResult>(...)`
     / `DefineStreaming<...>(...)` (extension operations), passing an
     optional `A2AOperationValidator<TRequest>` for semantic validation.
   - Operation ids are stable strings; for standard operations they follow
     the `https://a2a-protocol.org/operations/<kebab-case-name>` pattern.

3. **Register a handler** in `A2AStandardOperationHandlers.cs` (or your own
   handler-registration extension):
   - Call `builder.Map(standard.NewOperation, handlerLambda)` for unary, or
     `builder.MapStreaming(standard.NewOperation, handlerLambda)` for
     streaming.
   - The handler lambda receives `(A2AOperationContext context, TRequest
     request, CancellationToken cancellationToken)` and typically forwards
     to `context.RequestHandler` (an `IA2ARequestHandler`). If the protocol
     surface itself is changing, add the corresponding method to
     `IA2ARequestHandler` first.

4. **Bind the operation to each transport you need to support:**
   - **JSON-RPC** — in `A2AStandardJsonRpcBindings.cs`, add a
     `.Map(...)`/`.MapStreaming(...)` call to
     `AddStandardA2AJsonRpcBindings`, giving the JSON-RPC method name.
   - **HTTP+JSON** — in `A2AStandardHttpBindings.cs`, add a
     `.Map(...)`/`.MapStreaming(...)` call to `AddStandardA2AHttpBindings`,
     giving the HTTP method, route template, a request binder
     (`HttpContext -> TRequest`), and the result's `JsonTypeInfo<TResult>`
     from `A2AJsonUtilities.DefaultOptions`. Also add the route to
     `CanonicalRouteOperationIds` so the reserved-route validation stays in
     sync.

5. **Declare known errors**, if applicable, with
   `operationCatalogBuilder.DeclareError<TRequest, TResult, TDetails>(operation, errorId)`,
   then map each declared error to an HTTP status via
   `httpBindingBuilder.MapError(operation, error, statusCode, detailsTypeInfo)`.

6. **Add tests** mirroring the existing suites in
   `tests/A2A.UnitTests/Operations/` and
   `tests/A2A.UnitTests/AspNetCore/`:
   - Catalog/handler tests: operation is discoverable, handler invokes the
     right `IA2ARequestHandler` method, validator rejects bad input.
   - Transport tests: JSON-RPC method dispatch, HTTP route dispatch
     (success + error paths), serialization round-trip.

7. **Update the agent card / spec references** if the new operation affects
   capability negotiation or public documentation.

> **gRPC parity:** if the new operation adds a method to `IA2ARequestHandler`
> itself (i.e. it's a core protocol operation, not a custom/extension
> operation), also add the matching RPC to the vendored `.proto`, regenerate
> the gRPC contract, and implement/override it in `A2AGrpcService`
> (`src/A2A.Grpc.AspNetCore/A2AGrpcService.cs`) — otherwise the gRPC binding
> silently falls behind JSON-RPC/HTTP+JSON. Custom/extension operations
> registered only via the operation catalog don't apply to gRPC, since its
> contract is fixed by the proto definition.

### Sequence: wiring a new operation at startup

```mermaid
sequenceDiagram
    participant Dev as You
    participant OCB as A2AOperationCatalogBuilder
    participant HCB as A2AOperationHandlerCatalogBuilder
    participant JB as A2AJsonRpcOperationBindingBuilder
    participant HB as A2AHttpOperationBindingBuilder

    Dev->>OCB: DefineStandardUnary<TRequest,TResult>(id, validator)
    OCB-->>Dev: A2AOperation<TRequest,TResult> handle
    Dev->>OCB: Build()
    OCB-->>Dev: A2AOperationCatalog

    Dev->>HCB: Map(handle, (ctx, req, ct) => ...)
    Dev->>HCB: Build(operationCatalog)
    Note right of HCB: Throws if handle/id/kind/types<br/>don't match a catalog entry

    Dev->>JB: Map("newOperationMethod", handle, ...)
    Dev->>JB: Build(operationCatalog)

    Dev->>HB: Map(HttpMethod, "/route", handle, binder, typeInfo)
    Dev->>HB: Build()
    Note right of HB: Validate() cross-checks bindings<br/>against the operation catalog at endpoint-mapping time
```

## Modifying an existing operation

Changing an existing operation is riskier because it can break wire
compatibility. Before changing a request/result shape, confirm whether the
change is additive (new optional field) or breaking (removed/renamed
field, changed semantics, changed route).

Typical steps:

1. **Update the DTO** (`TRequest`/`TResult`/`TEvent`) — prefer additive,
   backward-compatible changes (new optional properties with sensible
   defaults). Mirror any JSON property renames/aliases needed for
   compatibility.
2. **Update the validator**, if semantic validation rules changed
   (`A2AOperationValidator<TRequest>` passed to `DefineStandardUnary`/
   `DefineStandardStreaming`).
3. **Update the handler** in `A2AStandardOperationHandlers.cs` if the
   mapping to `IA2ARequestHandler` changed.
4. **Update transport bindings**:
   - If the route/method name must change, remember routes are
     validated against `CanonicalRouteOperationIds` — update that table
     too, or the reserved-route check will throw.
   - If request binding logic changed (e.g., a new query parameter), update
     the binder function in `A2AStandardHttpBindings.cs` /
     `A2AStandardJsonRpcBindings.cs`.
5. **Do not change `A2AOperationId` casually** — it is the stable identity
   used to match operation definitions, handlers, and bindings together.
   Changing it is equivalent to introducing a new operation and removing
   the old one.
6. **Update/extend tests** for every layer touched (catalog, handler,
   JSON-RPC binding, HTTP binding) and verify round-trip JSON compatibility
   with any existing captured payloads.
7. **Run the full test suite** for the affected projects:
   ```powershell
   dotnet test tests/A2A.UnitTests/A2A.UnitTests.csproj
   ```

## Validation safety nets

The catalogs intentionally fail fast at *build* time (during app startup),
not at request time, so misconfigurations are caught immediately:

- `A2AOperationHandlerCatalogBuilder.Build(operationCatalog)` throws if a
  registered handler's id/kind/request-type/result-type doesn't match an
  entry in the supplied `A2AOperationCatalog`, or if the handle doesn't
  belong to that catalog.
- `A2AHttpOperationBindings.Validate(handlerCatalog.OperationCatalog)` /
  the JSON-RPC equivalent cross-check that every bound operation exists in
  the catalog before `MapA2A`/`MapHttpA2A` registers routes.
- `A2AStandardHttpBindingBuilderExtensions.ValidateReservedRouteBinding`
  prevents a custom binding from hijacking a canonical standard route
  (e.g. you cannot remap `GET /tasks/{id}` to a non-standard operation).

If you see one of these exceptions while wiring up a new or modified
operation, it means a mismatch between the operation catalog, handler
catalog, or transport bindings — re-check that you're passing the exact
same typed handle (`standard.X`) to every builder.

## Quick reference: where to add code for a new standard operation

| Step | File |
|---|---|
| Request/result DTOs | `src/A2A/*.cs` (new or existing models) |
| Operation definition | `src/A2A/Operations/A2AStandardOperations.cs` |
| Handler | `src/A2A/Operations/A2AStandardOperationHandlers.cs` |
| `IA2ARequestHandler` method (if new) | `src/A2A/IA2ARequestHandler.cs` |
| JSON-RPC binding | `src/A2A.AspNetCore/A2AStandardJsonRpcBindings.cs` |
| HTTP+JSON binding | `src/A2A.AspNetCore/A2AStandardHttpBindings.cs` |
| Unit tests | `tests/A2A.UnitTests/Operations/`, `tests/A2A.UnitTests/AspNetCore/` |
