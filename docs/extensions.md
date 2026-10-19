# Extension negotiation

Register an extension as a supported capability, then negotiate its activation
separately for each request. The same registry can own custom operations exposed
through JSON-RPC, HTTP+JSON and gRPC.
The examples use the v1 bindings.

This guide describes the APIs in this source checkout. The
[runnable sample](../samples/ExtensionNegotiation/README.md) uses project references
and checks all three transports without model credentials.

## Support and activation

An extension registration is the agent's support declaration. Its URI, description,
required flag and parameters are copied into an immutable registration when
`RegisterExtension` runs. An extension does not need to own an operation: it may
describe a profile used with standard A2A operations.

A caller requests activation with the `A2A-Extensions` HTTP service parameter,
or `a2a-extensions` gRPC metadata. Only requested, registered extensions are
candidates for activation. A host availability policy can decline a candidate.
Without a policy, requested supported extensions activate.

After negotiation succeeds, an immutable `A2ARequestContext` contains the requested
identifiers and the activated subset. The activated set remains fixed for that
request, including its stream. Availability changes affect later requests; handlers
still need normal dependency-failure handling and authorization.

This follows the protocol's
[extension activation model](https://a2a-protocol.org/latest/topics/extensions/#extension-activation).
Negotiation is not an authorization mechanism.

## Register an extension and an operation

The following types and JSON metadata come from
[DebateOperations.cs](../samples/ExtensionNegotiation/DebateOperations.cs):

```csharp
var builder = new A2ACustomOperationRegistryBuilder();
var debate = builder.RegisterExtension(new AgentExtension
{
    Uri = "https://example.org/extensions/debate/v1",
    Description = "Demo operations for continuing a debate",
    Required = false,
});
var resume = builder.Map<ResumeDebateRequest, ResumeDebateResult>(
    debate,
    new A2AOperationId("https://example.org/operations#resume-debate"),
    (context, request, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activation = context.GetRequiredFeature<A2ARequestContext>();
        return ValueTask.FromResult(new ResumeDebateResult(
            request.DebateId, activation.ActivatedExtensions.Contains(debate.Uri)));
    },
    DebateJsonContext.Default.ResumeDebateRequest,
    DebateJsonContext.Default.ResumeDebateResult);
var registry = builder.Build();
```

The extension and its operation handles must belong to this builder. An unregistered
extension is not supported, even if an Agent Card or request names it. Parameters,
when supplied, must be a JSON object. Building freezes the registrations; rebuild
the registry when the supported capability set changes.

Extension identifiers compare exactly and case-sensitively. URI spelling, escaping,
case and version are not normalized. For example, support for `.../debate/v1` does
not activate `.../debate/v2`. Unknown requested identifiers are not activated.

An extension-owned operation requires that extension to be active before its
validator or handler runs. Direct typed registry calls enforce the same guard.
An operation without an owner can still run without an optional extension.

## Bind JSON-RPC and HTTP+JSON

Configure request availability with `A2AExtensionOptions`:

```csharp
var extensions = new A2AExtensionOptions(registry)
{
    CanActivate = (httpContext, extension, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Replace with the host's tenant, configuration or dependency check.
        return ValueTask.FromResult(true);
    },
};
var rpcBindings = new A2AJsonRpcCustomOperationBuilder()
    .Map("debate/resume", resume)
    .Build(registry);
var httpBindings = new A2AHttpCustomOperationBuilder()
    .Map(HttpMethods.Post, "/debate:resume", resume,
        async (context, ct) =>
            await JsonSerializer.DeserializeAsync(context.Request.Body,
                DebateJsonContext.Default.ResumeDebateRequest, ct)
            ?? throw new A2AException("A debate request is required.", A2AErrorCode.InvalidParams))
    .Build(registry);

app.MapA2A(requestHandler, "/rpc", extensions, rpcBindings);
app.MapHttpA2A(requestHandler, "/http", extensions, httpBindings);
```

The optional custom mappings do not replace standard operations. Negotiation also
applies to standard operations on these registry-aware endpoints. Standard agents
receive the snapshot through `RequestContext.ExtensionActivation`; custom request
handlers can opt into `IA2ARequestHandlerWithContext`. Custom operations get it
through `A2ACustomOperationContext`. Transport features remain available to hosts.

For easy-path DI hosting, register with
`services.AddA2AAgent<MyAgent>(extensions, agentCard)`. The DI overload
`app.MapA2A("/rpc")` then uses those options for standard JSON-RPC operations.
Use the explicit mappings above when exposing custom operations as well.

The old handler-only overloads remain negotiation-free. Registry-and-bindings
overloads use negotiation with no availability callback. Low-level processor
consumers can opt in with `A2AJsonRpcProcessor.ProcessRequestWithExtensionsAsync`;
the existing `ProcessRequestAsync` signatures are unchanged.

## Bind gRPC

Use the same registry with transport-specific availability context:

```csharp
builder.Services.AddA2AGrpc(new A2AGrpcExtensionOptions(registry)
{
    CanActivate = (callContext, extension, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    },
});

// Register IA2ARequestHandler separately, for example with AddA2AAgent.
app.MapGrpcA2A();
app.MapGrpcA2ACustomOperations();
```

This enables negotiation on the standard A2A service and the SDK's generic custom
operation service. It does not intercept unrelated gRPC services. The
parameterless `AddA2AGrpc()` remains negotiation-free.

`AddA2AGrpcCustomOperations(registry)` scopes negotiation to the generic custom
operation service. It does not enable required-extension checks on standard A2A
operations. Use the explicit `AddA2AGrpc(options)` overload above when both services
should negotiate, rather than combining the two registrations.

For a dedicated protobuf service, opt in with
`services.AddGrpc().WithA2AExtensions<MyGrpcService>(options)`. In its handler,
pass `callContext.CreateA2AOperationContext()` when invoking a registry operation.
The helper supplies features; it does not negotiate on its own. Map that service's
endpoint separately.

These hooks cover unary and server-streaming calls. Request and response metadata
use `a2a-extensions`. Successful negotiation stages initial response metadata and
carries the same snapshot in final trailers, including RPC errors.

## Read activation in standard agents and handlers

An `IAgentHandler` can use the same code for JSON-RPC, HTTP+JSON and gRPC:

```csharp
public async Task ExecuteAsync(
    RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
{
    bool active = context.ExtensionActivation?.ActivatedExtensions.Contains(
        "https://example.org/extensions/debate/v1") == true;
    await new MessageResponder(eventQueue, context.ContextId).ReplyAsync(
        active ? "Debate extension active" : "Normal response",
        cancellationToken: cancellationToken);
}
```

`A2AServer` copies the negotiated snapshot into the agent context for send, streaming
send and task cancellation. It retains the snapshot when execution continues after
the initial response or stream disconnect. The snapshot is not stored on the task
or conversation, and a later request negotiates independently. `null` means there
was no negotiation; a negotiated context can instead have an empty activated set.
No HTTP/gRPC accessor or ambient request state is required by the agent.

For a custom standard request handler, implement `IA2ARequestHandlerWithContext`
and override the three-argument operations that consume activation. Each default
implementation forwards to its legacy operation, so unmodified
`IA2ARequestHandler` implementations continue to work. The added cancellation-token
arguments are deliberately required to avoid ambiguity in existing two-argument
calls. For an `A2AServer` subclass, override the context-aware send/cancel overloads
when customizing negotiated execution as well as the legacy path.

Registry-aware hosting binds the context automatically in all three transports.
A host calling handlers directly can bind a snapshot explicitly:

```csharp
var snapshot = await registry.NegotiateExtensionsAsync(
    ["https://example.org/extensions/debate/v1"], cancellationToken: cancellationToken);
var requestHandler = handler.WithRequestContext(snapshot);
await requestHandler.SendMessageAsync(request, cancellationToken);
```

The adapter does not modify the shared handler. It captures only the immutable SDK
context, not transport objects or request-scoped services. A handler that does not
opt into the context-aware interface is returned unchanged.

## Required extensions and failures

Set `Required = true` only when clients must comply with the extension for the
agent's requests. A missing required activation fails with
`A2AErrorCode.ExtensionSupportRequired` before handler execution, including for
standard operations. A policy declining a required extension fails negotiation.
Do not mark data-only declarations as required.

Declining an optional extension does not block an unrelated standard operation,
but an operation owned by that extension cannot run. Policy exceptions and request
cancellation are failures, not declined activation. A failed negotiation publishes
no partial active set. Once negotiation succeeds, a later operation error retains
that request's activation metadata.

The callback runs once per requested supported extension for the request. It should
honor the supplied cancellation token. The SDK does not re-run it between streamed
events or implement extension dependencies, version fallback or distributed leases.

## Agent Card consistency

Keep discovery aligned with the authoritative registry:

```csharp
app.MapWellKnownAgentCard(registry, agentCard);

// Or prepare a detached card for another hosting path.
var hostedCard = registry.PrepareAgentCard(agentCard);
registry.ValidateAgentCard(hostedCard);
```

An unsigned card with no extension declarations derives them from the registry.
An explicit list must contain the complete supported set, with matching declarations
and parameters. Duplicates, unknown entries and mismatches are rejected.
`PrepareAgentCard` copies the whole card rather than mutating the host's input.
`ValidateAgentCard` checks without deriving or changing declarations.

For signed cards, preparation preserves existing content and signatures. A
declaration mismatch is rejected instead of silently rewriting the card or removing
its signatures. Update the declarations and re-sign the card before hosting it.
Prepare an unsigned card first if the host will sign it later.

The extension's non-optional protobuf scalar fields have implicit defaults:
an omitted `required` and `false` are equivalent, as are an omitted description
and an empty description. Consistency checks accept these equivalents without
rewriting the supplied representation. The protocol's
[canonicalization rules](https://a2a-protocol.org/latest/specification/#841-canonicalization-requirements)
distinguish those defaults from fields declared with protobuf `optional`.
This API checks registry consistency, not JWS authenticity; it does not generate or
cryptographically verify signatures.

The registry-aware well-known mapping validates during setup. The extension-aware
`AddA2AAgent` overload prepares its registered card before modifying the service
collection. Extended cards returned by `IA2ARequestHandler` are checked at response
time in all three bindings. A per-request availability decline does not remove
support declarations from the card.

### Compose one declaration conditionally

When a host composes a card independently of the complete-registry hosting APIs,
it can choose to advertise one registered capability based on agent configuration:

```csharp
if (agentUsesDebate)
{
    agentCard = debate.EnsureAgentCardDeclaration(agentCard);
}
```

Here `debate` is an `A2AExtensionRegistration`. The helper returns a detached copy
without modifying the input. On an unsigned card, it adds a missing declaration;
an existing declaration must match the registration. Matching declarations retain
their supplied representation, including implicit scalar defaults and structurally
equivalent JSON parameters. Duplicate declarations for that exact URI and
conflicting content are rejected. Other declarations and their order are preserved;
they are not validated against this registration's registry.

A signed card must already contain exactly one matching declaration. Otherwise
the host must update and re-sign it; the helper never rewrites a signed card or
removes its signatures. It does not verify cryptographic signatures.

This is declaration composition only, not a per-agent runtime support scope.
Skipping the call does not remove a previously supplied declaration or disable
an extension. Negotiation, required-extension checks and operation dispatch still
use the registry as before. The complete-registry APIs above still reject a
partial declaration list; this helper does not bypass those hosting checks.

## Read activation on client errors

The standard SDK clients attach the peer's response activation to protocol errors:

```csharp
try
{
    await client.GetTaskAsync(new GetTaskRequest { Id = "missing-task" });
}
catch (A2AException error)
{
    bool wasActivated = error.ActivatedExtensions.Contains(
        "https://example.org/extensions/debate/v1");
    Console.WriteLine($"{error.ErrorCode}: debate active = {wasActivated}");
}
```

`A2AClient`, `A2AHttpJsonClient` and `A2AGrpcClient` preserve the operation error
code while exposing an immutable response snapshot. A missing or malformed response
parameter yields an empty set, not a guess based on the request. An empty set is
therefore not proof that an older peer declined activation. Caller cancellation
keeps its cancellation behavior instead of becoming an `A2AException`.

For gRPC, the initial activation metadata takes precedence; trailers are a fallback
when that parameter was not reported initially. Conflicting lists are not combined.
For streaming, the initial snapshot is retained across later operation errors.

## Client integration boundaries

The built-in `IA2AClient` contract still represents standard A2A operations. Server
operation handles do not create typed custom-operation client methods. Use the
extension's wire contract for custom calls, as the sample does with raw HTTP
requests and the SDK's generated generic gRPC envelope.

The response-error property does not add a successful-response metadata wrapper or
a typed per-request extension-options API. HTTP callers can set the service
parameter on their own transport; generated gRPC clients can use per-call
`Metadata`. Avoid changing shared `DefaultRequestHeaders` to select different
extensions for concurrent callers. Keep authentication and dependency recovery
separate from this negotiation layer.
