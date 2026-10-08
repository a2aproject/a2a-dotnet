# V1 push notifications

`A2AServer` can store task-scoped webhook registrations and publish task events
through a bounded, process-local HTTP sender. This feature targets A2A **1.0**.
The protocol requirements come from the official v1 specification and proto;
storage layout, retention, queue limits, and retry numbers are SDK policies.

## Enable the feature

```csharp
using A2A;
using A2A.AspNetCore;

builder.Services.AddSingleton(new PushNotificationUrlOptions
{
    AllowedHosts = ["callbacks.example.com", "*.trusted.example"],
});
builder.Services.AddA2AAgent<MyAgent>(new AgentCard
{
    // Include the other card fields required by your application.
    Capabilities = new AgentCapabilities { PushNotifications = true },
});
```

`AddA2AAgent` registers replaceable defaults with `TryAddSingleton`. Registration
alone does not enable push: false/omitted capability rejects CRUD and inline
push settings with `PushNotificationNotSupported`. Keep the registered card
fixed for the server's lifetime.

The original five-argument `A2AServer` constructor remains available without
push. Direct construction with push uses the overload accepting a card, store,
destination policy, and sender. Dispose directly owned servers/senders.

## Protocol requirements and SDK choices

| Area | Requirement | SDK policy |
|---|---|---|
| Wire payload | One `StreamResponse` payload and configured authentication; 2xx acknowledges receipt | Source-generated A2A JSON, no JSON-RPC envelope |
| Attempts | At least one attempt for configured notifications; finite retry exhaustion is allowed | Bounded admission with backpressure, three attempts by default |
| Response kind | An agent may return a `Task` or a direct `Message` | Preserve the handler's choice; no automatic task synthesis |
| Config lifetime | Persist until task completion or explicit deletion; delete is idempotent | Retain after completion/failure until explicitly deleted or removed by application retention |
| Pagination | Consistent shared semantics; proto3 zero has no distinct presence | Zero/omitted defaults to 50, cap 100; negative is invalid |
| Security | Configured authentication and host authorization; HTTPS/SSRF protection are recommended | Fail-closed HTTPS/public-address policy and connection enforcement |
| Read secrets | Protect credentials | Return accepted create values; redact token/credentials on get/list |

The sender uses `application/json` for compatibility with v1.0.0 receivers.
V1.0.1 prefers `application/a2a+json`; this is not an exclusive media-type
prohibition. The sample receiver accepts both UTF-8 JSON media types.

## Configuration operations

Use existing `IA2AClient` create/get/list/delete over JSON-RPC, HTTP+JSON, or gRPC:

```csharp
var created = await client.CreateTaskPushNotificationConfigAsync(
    new TaskPushNotificationConfig
    {
        TaskId = task.Id,
        Url = "https://callbacks.example.com/a2a",
        Authentication = new AuthenticationInfo
        {
            Scheme = "Bearer",
            Credentials = credential,
        },
    }, cancellationToken);
```

- The task must exist in the host-authorized task scope.
- Omitted config IDs are assigned a GUID. Duplicate supplied IDs are rejected
  atomically; create is not an update operation.
- Supplied IDs must be one path segment: `/`, `\`, `%`, control characters,
  all-whitespace values, and the complete dot segments `.` and `..` are rejected
  with `InvalidParams` before storage or inline execution. Percent signs are
  excluded to prevent encoded-separator aliases in REST routing. Other URI
  punctuation, Unicode, embedded spaces, and ordinary dots are supported through
  the SDK's URI escaping. This is an SDK binding-safety rule, not a UUID requirement.
- The HTTP+JSON route task ID is authoritative.
- Get/list redact `Token` and `Authentication.Credentials`, retaining the
  scheme without changing stored delivery values.
- The development store orders IDs and uses authenticated, task-bound cursors.
  A mutation invalidates existing cursors; restart pagination on `InvalidParams`.
- A registration created after task completion receives the current terminal
  `Task` snapshot. It remains readable until explicit removal. This is an SDK
  policy, not a protocol-required automatic cleanup rule.
- Delete invalidates its registration generation before returning. Queued work,
  retries, and cancellable in-flight requests observe the deletion signal.
  Already-transmitted bytes and remote processing cannot be recalled.

Each recreated ID gets a new opaque generation. Old work never borrows the
replacement's URL/credentials. Custom stores must provide a generation-scoped
`RemovalToken` on snapshots and signal it for deletion; distributed stores must
coordinate invalidation across their actual deployment.

## Inline settings and task/message semantics

Inline `SendMessageConfiguration.TaskPushNotificationConfig` is validated before
the handler. Its task association uses the server-resolved task ID.

- A real task activates the pending config with its first persisted event.
- A direct-message-only handler remains a direct message. No hidden task or
  registration is created; a debug event reports the unused inline config.
- Failure before a task exists does not activate a config.
- Requests without push retain their normal response kind.

The explicit execution mode controls task responses: blocking requests end at a
terminal/interrupted state; `ReturnImmediately = true` permits an in-progress
task. A blocking handler that ends with only a working/submitted task is an
invalid agent response, not successful completion. Streaming continuations
begin with the current task snapshot. Response history limits are applied
without trimming stored history or independently configured webhook payloads.

## Persistence, admission, and ordering

The shared task lock remains valid while any holder or waiter exists; removing
the last SSE subscriber does not create a second mutual-exclusion domain.

Under that boundary, the SDK checks active configs before committing an update,
persists task state, activates pending inline config, and captures immutable
generation-bound publication records. SSE publication retains its state order.
Per-task enqueue turns preserve the same order outside the lock.

No network call, retry delay, or queue-capacity wait occurs under the state lock.
The bounded agent event queue and push queue can propagate backpressure.
`ReturnImmediately` does not wait for task completion or webhook acknowledgment,
but admission can be delayed by resource pressure.

**These are separate stores/queues, not one transaction.** A config-store failure
or admission error is surfaced, not treated as an empty successful publication.
An error after task persistence does not imply rollback; inspect the committed
task/config state. A durable sender alone cannot recover the crash window before
enqueue. Atomic durable publication requires application integration with a
shared transaction/outbox, not a delivery-guarantee claim by this SDK.

## Sender policy and lifetime

`HttpPushNotificationSender` has one owned worker:

| Setting | Default |
|---|---|
| HTTP attempt timeout | 15 seconds |
| Total attempts | 3 |
| Retry delay / multiplier / cap | 1 second / 2 / 30 seconds |
| Buffered items, excluding active delivery | 256 |
| Full queue | Wait outside task locks |
| Graceful drain before shutdown cancellation | 5 seconds |
| Optional compatibility header | `X-A2A-Notification-Token` |

Override `PushNotificationDeliveryOptions` before `AddA2AAgent`. Explicit
`Reject` is an **admission error**, not accepted delivery or permission to delete
the config. Applications choosing it must handle partial persistence/error
semantics. Network failures, timeouts, 408, 429, and 5xx may retry; other 4xx and
redirects do not. HTTP failure never rewrites the agent's task state.
If event publication fails, its owner closes the abandoned agent queue, cancels
and joins the producer, and reports the error before releasing its lifetime.
An admission failure alone does not transition the committed task to Failed.

The selected sender is started/stopped by hosting; there is no second default
worker alongside a replacement. Each stock-server execution owns both its
producer and its persistence/publication reader. A bounded response channel
preserves backpressure while an SDK stream is active; disconnect or shutdown
detaches that response consumer without abandoning the producer's event queue.
Normal hosted stop joins active and detached executions, including cooperative
handler cleanup and pending publication, before closing sender admission.

A host deadline is different from completed quiescence: stop throws cancellation,
logs unfinished executions, prevents new sender calls, and cancels pending
admission. Unprocessed events can be abandoned; no rollback or successful
delivery is implied. Producer ownership is retained until it actually exits.
Direct server disposal has no deadline and waits for cooperative handlers;
custom handlers/dependencies that ignore cancellation can delay that disposal.
Directly used senders can start on first enqueue and must be disposed. Shutdown
does not erase configurations.

Queued work is in memory: crashes, shutdown deadlines, permanent failure, and
explicit deletion can prevent receipt. There is no guaranteed remote receipt,
restart durability, exactly-once processing, or cross-process ordering. Receivers
must be idempotent. Custom dependencies must honor cancellation.

## Destination and credential boundaries

The URL policy requires an absolute HTTPS URL without userinfo/fragment and
only approved public DNS answers. Private, loopback, unspecified, link-local,
multicast, reserved/documentation and tunnel ranges are excluded. Exact hosts
and explicit `*.example.com` subdomains are supported; empty/`*` lists disable
only host filtering, not address checks. Use ASCII/punycode host entries.

`IPushNotificationUrlValidator.ValidateAsync` returns approved addresses. The
default connector runs the selected policy at connection establishment and
dials only those IP endpoints—no unchecked DNS re-resolution/fallback. It
retains the original URI for TLS/SNI and normal certificate validation.
Each attempt is also revalidated, including reuse of a previously approved
pooled connection.

The default named client disables redirects, cookies, ambient proxies, and
factory URL logging. Replacing its handler must preserve these protections and
the connection policy. Application-wide HTTP instrumentation also needs its
own URL/secret redaction. Use egress controls as defense in depth.

Authentication is per request, never shared default headers. Validate header
values before storage. The optional compatibility token header does not replace
Authorization and is not full v0.3 support. Get/list redaction and hashed
correlation IDs in push diagnostics do not replace access control.

## Authorization and replacements

The default in-memory stores are for development or one trusted agent boundary.
Task existence is **not** caller authorization. The host must authorize before
raw lookup and keep task/push stores in the same authorized scope. Caller-supplied
`Tenant`, context IDs, and GUID knowledge are not authorization. Queued work must
not depend on a retained `HttpContext`.

Register application store/policy/sender replacements before `AddA2AAgent`.
The SDK does not add a tenant system, database, durable broker, encrypted
storage, OAuth refresh, quotas, or retention service. Advanced atomic/durable
deployments can compose their own request handler and outbox.

## Version scope

This implementation supplies **v1** push. Official v0.3 does define set/update,
get, list, delete and legacy webhook payload behavior; forwarding only set/get
to v1 is not full compatibility.

The compatibility processor rejects legacy push for the stock `A2AServer`.
Associate discovery with the same handler explicitly:

```csharp
app.MapA2AWithV03Compat(server, "/agent");
app.MapAgentCardGetWithV03Compat(server, () => Task.FromResult(card), "/agent");
```

The associated stock handler's legacy/blended card view suppresses push;
explicit v1 discovery retains the v1 capability. An unrelated DI handler never
changes another agent's card. The original card-factory-only overload is
preserved and leaves version-specific declarations to its caller.
Custom handlers/subclasses keep their existing behavior and are responsible for
their version-specific contracts. Manually composed handlers/card callbacks
must likewise advertise only capabilities actually supplied. Full legacy push
parity is a separate, unselected scope.

See the [owned-loopback demo](../samples/PushNotificationReceiver/README.md) and
the [security guide](security.md).
