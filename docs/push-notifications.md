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

### Local development callbacks

`PushNotificationUrlOptions.AllowLoopback` defaults to `false`. Explicitly enable
it to receive callbacks at URLs such as `http://localhost:5000/notify` without a
public tunnel. To opt in only during development, replace the options registration
above with:

```csharp
builder.Services.AddSingleton(new PushNotificationUrlOptions
{
    AllowedHosts = ["callbacks.example.com", "localhost", "127.0.0.1", "::1"],
    AllowLoopback = builder.Environment.IsDevelopment(),
});
```

Register these options before `AddA2AAgent`. The SDK does not inspect the hosting
environment or automatically relax its policy in Development.

The exception permits HTTP or HTTPS only for `localhost` and loopback IP literals
(IPv4, IPv6, and IPv4-mapped IPv6). Every DNS answer for `localhost` must be
loopback. Other hostnames that resolve to loopback, LAN/private addresses,
link-local/metadata addresses, and public HTTP destinations remain blocked.
Host allowlists and connection-time address validation still apply; HTTPS still
requires a valid certificate.

Only enable this for trusted local development callers: registrations can target
other services on the server's loopback interface, and HTTP sends configured
credentials without TLS. The callback must be reachable from the server's network
namespace; `localhost` inside a container refers to that container, not its host.

## Protocol requirements and SDK choices

| Area | Requirement | SDK policy |
|---|---|---|
| Wire payload | One `StreamResponse` payload and configured authentication; 2xx acknowledges receipt | Source-generated A2A JSON, no JSON-RPC envelope |
| Attempts | At least one attempt for configured notifications; finite retry exhaustion is allowed | Bounded admission with backpressure, three attempts by default |
| Response kind | An agent may return a `Task` or a direct `Message` | Preserve the handler's choice; no automatic task synthesis |
| Config lifetime | Persist until task completion or explicit deletion; delete is idempotent | Retain after completion/failure until explicitly deleted or removed by application retention |
| Pagination | Consistent shared semantics; proto3 zero has no distinct presence | Zero/omitted defaults to 50, cap 100; negative is invalid |
| Security | Configured authentication and host authorization; HTTPS/SSRF protection are recommended | Fail-closed HTTPS/public-address policy by default, explicit loopback opt-in, and connection enforcement |
| Read secrets | Protect credentials | Return accepted create values; redact token/credentials on get/list |

The sender uses `application/a2a+json`, following the
[current push-notification payload format](https://a2a-protocol.org/latest/specification/#433-push-notification-payload).
The [v1.0.0 example](https://a2a-protocol.org/v1.0.0/specification/#433-push-notification-payload)
used `application/json`. Receivers restricted to that media type need to accept
`application/a2a+json` when using this sender. The sample receiver accepts both
UTF-8 JSON media types for compatibility.

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

### Proactive continuation

The hosting process can resume an existing task without an inbound A2A request:

```csharp
var result = await server.ContinueTaskAsync(
    taskId,
    async (context, cancellationToken) =>
    {
        await context.EventQueue.EnqueueMessageAsync(new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            TaskId = context.TaskId,
            ContextId = context.ContextId,
            Parts = [Part.FromText("The long-running job has completed.")],
        }, cancellationToken);

        await new TaskUpdater(context.EventQueue, context.TaskId, context.ContextId)
            .CompleteAsync(cancellationToken: cancellationToken);
    },
    cancellationToken);
```

This is a **hosting API**, not a protocol operation. The host must authorize the
external trigger and resolve it to an allowed task before calling the server.
Use the same configured server instance and task/push services as inbound requests.
No synthetic client request, user message, history entry, or HTTP loopback is needed.

Before constructing `TaskContinuationContext` or invoking the producer, the server
loads the authoritative task and checks that it is non-terminal and has at least
one active registration for that exact task ID. "Active" means present and not
deleted; expiry and enablement policies belong to the configured store. A missing
task, terminal task, or missing registration fails without running the producer.
Push must be enabled on the server's agent card; disabled or unconfigured push
cannot supply an active delivery route.

The producer receives an independent task snapshot, `AgentEventQueue`, and
`RequestContext`. `RequestContext.Task`, `TaskId`, and `ContextId` identify the
existing task. `IncomingMessage` and `UserText` are null. The existing non-null
`Message` accessor throws `InvalidOperationException` when there is no inbound
message; proactive-aware integrations must use `IncomingMessage`. Configuration,
request metadata, and extension activation are null: no client request exists,
and these request-local settings are not reconstructed from task history.
Task metadata, artifacts, history, and status remain available on the task snapshot.
Mutating the snapshot alone does not save updates; emit events through the queue.

All four event payloads (`Task`, `Message`, status update, artifact update) are
supported. Each event must contain exactly one payload with the continuation's
task ID and context ID. Full `Task` events retain their normal replacement
semantics; include any history, artifacts, and metadata that must be preserved.
Events use the same projection, persistence, subscriber notification, and
ordered push-admission path as inbound execution, including multi-registration
fan-out. `TaskUpdater` terminal/interruption helpers close the event queue;
emit other updates before calling them.

The terminal states are Completed, Canceled, Failed, and Rejected. Neither inbound
nor proactive execution can start a new turn on a terminal task. During an already
running turn, the first persisted terminal state wins, for both status updates
and full-task events. A conflicting later state is not persisted or published.
Proactive execution reports `NonContinuableTask` rather than silently dropping
such an event. Messages, artifacts, and updates retaining the same terminal state
may still be processed by that running turn. This is not a complete transition
graph. Terminal notification closes existing subscriber streams as usual.

`TaskContinuationResult` reports the final persisted task, `PersistedEventCount`,
and `AcceptedPushNotificationCount`. The latter counts **event/registration
pairs**, not events or HTTP acknowledgments. A successful producer must persist
at least one event, and each persisted event must be accepted for at least one
registration. Successful completion waits for production and publication to end,
but **does not wait for webhook receipt**.

`TaskContinuationException.FailureKind` distinguishes missing task, missing
registration, terminal state, producer, task-store, push-store, projection,
invalid event identity/payload, cancellation, admission, and no-event failures.
`InnerException` preserves the underlying failure where one exists. `Progress`
contains the last successfully persisted state and counts (or is null when the
initial task could not be loaded). Persistence and earlier subscriber publication
are **not rolled back**. Cancellation is also reported through this exception
with `FailureKind.Cancellation` and an underlying `OperationCanceledException`,
so committed progress is not lost. Invalid API arguments use argument exceptions;
starting after server shutdown uses `ObjectDisposedException`.

Registration availability is checked again during publication. Deletion before
admission is not counted as acceptance, even when an earlier event already
persisted or another registration accepted that event. Recreating an ID never
redirects the old generation's event to the replacement. Deletion after acceptance
may suppress subsequent delivery attempts; previously accepted counts do not
promise remote delivery. Custom senders must throw `OperationCanceledException`
with the registration's removal token when deletion prevents admission, rather
than returning success.

### Execution coordination

Inbound message execution, cancellation handlers, and proactive continuations
sharing a `ChannelEventNotifier` serialize **whole executions per task**, including
production, persistence, and push admission. They do not interleave task events.
The server reloads existing tasks after acquiring this execution gate, so queued
producers see the previous execution's committed state. Different task IDs remain
concurrent. The execution gate is separate from the short-lived persistence lock
and is never held in place of that lock while consuming the bounded event queue.

Streaming disconnect and `ReturnImmediately` do not release the execution gate
while their producer/publication work is still running. Slow producers or push
backpressure therefore delay later executions for the same task. Cancel requests
signal tracked detached/proactive producers, then wait for their publication and
cleanup before running the cancel handler. Explicit task cancellation can interrupt
pending detached admission; graceful shutdown still drains inbound cleanup events
unless its deadline expires. Synchronous inbound executions finish
before a queued cancel handler runs. If the earlier execution reached a terminal
state, the queued cancel is rejected as not cancelable. Producers must not await
nested same-task executions, which would wait on their own gate.

Proactive execution participates in normal server execution tracking and shutdown.
Cancellation closes blocked queue writers, cancels production/admission, and joins
the producer before reporting progress. Producers and custom services must honor
cancellation; a producer that ignores cancellation can delay completion/disposal.

Coordination is **process-local and notifier-scoped**, not cross-process.
Multiple servers must share the notifier and authorized stores to share these
gates. Independent notifiers or server processes are not coordinated.
`ITaskStore` has no compare-and-swap/version contract: distributed serialization,
transactional outboxes, and restart durability require host-provided integration.

### Event persistence boundary

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

`IPushNotificationSender` is the replacement point for durable acceptance,
restart recovery, persistent retries, and dead-letter handling. See
[Implementing a durable push notification sender](durable-push-notifications.md)
for its contract, registration/lifecycle integration, security requirements,
and the separate transactional-outbox requirement.

## Destination and credential boundaries

By default, the URL policy requires an absolute HTTPS URL without userinfo/fragment and
only approved public DNS answers. Private, loopback, unspecified, link-local,
multicast, reserved/documentation and tunnel ranges are excluded. Exact hosts
and explicit `*.example.com` subdomains are supported; empty/`*` lists disable
only host filtering, not address checks. Use ASCII/punycode host entries.
The [local development opt-in](#local-development-callbacks) permits HTTP/HTTPS
loopback callbacks only; it does not disable the other destination checks.

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

## Implementing a persistent push notification store

The SDK supplies only `InMemoryPushNotificationStore`. A custom
`IPushNotificationStore` is a **secret-bearing store**, not just a database of
webhook addresses. Persisting registrations does not make the stock sender's
queue durable, refresh credentials, or recover failed notifications.

The following guidance applies to v1 `IPushNotificationStore` implementations.
Task state is stored separately through `ITaskStore`; replacing that store does
not persist push registrations. The legacy v0.3 task-store contract is different.

### Protect credentials and stored configurations

Treat `TaskPushNotificationConfig.Token` and `Authentication.Credentials` as
secrets. The store receives unredacted values, and
`PushNotificationConfigSnapshot` serializes them as ordinary JSON bytes in
memory; it does not encrypt them. Its `Configuration` property returns a fresh,
secret-bearing copy.

- Encrypt credentials at rest and use authenticated, encrypted connections to
  the backing store. Include backups, replicas, exports, and temporary files in
  the protection and access-control policy.
- Manage encryption keys separately from stored records, using a managed key
  service or equivalent controls. Restrict decrypt permission to the delivery
  service and necessary administrative operations; plan key rotation and
  recovery. Database encryption alone does not prevent an authorized database
  reader from seeing plaintext columns.
- If using application-level encryption, bind ciphertext to the tenant, task,
  configuration ID, and registration generation, for example with authenticated
  associated data. Do not allow encrypted credentials to be transplanted into
  another registration.
- A store can keep a reference to a separately protected secret instead of its
  value, but must resolve it before returning a usable configuration snapshot.
  Pin the secret version to the registration generation: resolving "latest"
  must not silently change credentials in an existing generation.
- Use least-privilege service identities and restrict administrative access.
  Prefer short-lived, audience-restricted, single-purpose webhook credentials.
  Do not reuse a user's general A2A access token as a callback credential.

Protect callback URLs and ownership metadata as sensitive configuration too.
Do not embed credentials in URL paths or query strings. Cancellation of a
snapshot's `RemovalToken` does not erase its data or copies already in memory;
protect process dumps and diagnostic captures accordingly.

The [A2A push security guidance](https://a2a-protocol.org/latest/specification/#132-push-notification-security)
recommends secure credential storage, periodic token rotation, and unique,
single-purpose tokens for each configuration. These controls do not prescribe
a particular database, encryption provider, or token issuer.

### Enforce ownership and limit exposure

Derive the authorized tenant/principal scope from trusted host context, not
caller-supplied `Tenant`, task IDs, or configuration IDs. Persist ownership
metadata and enforce that scope on every read, list, create, delete, and delivery
lookup. Keep task and push stores in the same authorized scope. Background
delivery must resolve the correct scope without retaining an inbound
`HttpContext`; it must not search across tenants for a matching task ID.

Use parameterized queries and appropriate uniqueness constraints, including
the tenant scope when sharing a database. Apply per-tenant quotas and bounded
record sizes to prevent unbounded registration or secret storage. Scope opaque
pagination cursors to the authorized task/tenant and validate them; cursors are
not authorization credentials.

Never log serialized configurations, credentials, tokens, connection strings,
or secret-store responses. Review database command logging, tracing, exception
messages, audit exports, and HTTP request/response body capture. Audit access
and configuration changes using non-secret identifiers rather than secret
values or complete callback URLs.

The stock server redacts token/credential fields on public get/list responses,
but create returns accepted secrets. Internal store reads remain secret-bearing
because the sender needs those values. Return independent configurations so
public-response redaction cannot mutate stored delivery credentials.

### Preserve atomicity and registration generations

Implement the interface contract across all participating processes, not just
under a local lock:

- `SaveAsync` atomically creates a registration and rejects duplicate IDs; it
  is not an upsert. Give every recreated ID a new non-empty, opaque `Version`
  and a generation-scoped `RemovalToken`.
- `GetAsync` returns an independent snapshot or null for an absent registration.
  `GetAllAsync` returns an independent, atomic snapshot of active registrations
  for the task. Keep each generation's URL and credentials immutable.
- `DeleteAsync` is idempotent. A supplied version removes only that generation;
  stale cleanup must not delete a replacement registration. Signal that
  generation's snapshots before returning.
- Coordinate cache invalidation and removal signals across every process that
  can hold snapshots or send notifications. A database row deletion alone
  cannot cancel another process's local `CancellationToken`. Define behavior
  under network partitions; eventual cache expiry alone does not satisfy the
  deletion contract.
- Honor cancellation and surface storage, decryption, and invalidation failures
  explicitly, with secret-safe errors. Do not translate an unavailable store
  into an empty list, a missing record, or successful deletion.

The stock sender checks the current generation before attempts and observes
removal during delivery. Preserve those semantics in any custom sender.
Deletion cannot recall transmitted bytes or undo remote processing.

### Define retention, rotation, and recovery policies

Document retention for active and terminal tasks, abandoned registrations,
backups, and secret versions. Preserve configurations for the protocol-required
lifetime; do not silently evict active registrations to enforce a cache TTL.
Coordinate registration deletion and invalidation with task removal so task
cleanup does not leave credentials orphaned. If protocol lookup is no longer
possible after task deletion, provide an authorized administrative cleanup path.

Deleting a registration does not revoke the credential at its issuer or erase
backup copies. Define issuer revocation separately, account for retention
requirements, and ensure restoring a backup cannot reactivate deleted
registrations or revoked credentials.

The stock SDK does not acquire or refresh credentials. Create is not update:
rotation requires a new registration and deletion of the old one, or deletion
and recreation of the same ID. Creating first can overlap deliveries; deleting
first can leave a gap. Old queued work never adopts the replacement's
credentials or URL.

HTTP 401/403 permanently fail that delivery in the stock sender. Replacing
credentials does not replay it, and subsequent events can fail independently
while an invalid registration remains present. Monitor delivery failures
without recording secrets and arrange renewal before expiry. `GetTask` can
reconcile current task state and available artifacts/history, but is not an
event replay API.

Durable registrations are not a durable outbox. If loss is unacceptable, design
transactional task-state/outbox integration, bounded retries and retention,
authorized replay, and receiver deduplication separately. Any persisted outbox
also needs protection for payloads and credential-bearing snapshots. Replay
must not silently redirect an old generation's events to a new destination.

### Verify a custom implementation

Before production use, exercise tenant isolation (including background delivery
and pagination), secret-safe logs/errors, encryption/key rotation, and denied
decrypt access. Test concurrent duplicate creation, snapshot independence,
version-conditional deletion, recreation, and cross-process invalidation during
queued work, retries, and in-flight requests. Include backing-store outages,
network partitions, restart/backup restoration, credential expiry, and task
retention cleanup. Verify that failures are visible and that no test depends on
plaintext credentials appearing in diagnostics.

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
their version-specific contracts. A custom handler whose push implementation is
v1-only can implement `IV03PushNotificationCompatibility` and return `false`
from `SupportsV03PushNotifications`; the compatibility processor will then
reject legacy registration and suppress push in its associated legacy/blended
card. Manually composed handlers/card callbacks must likewise advertise only
capabilities actually supplied. Full legacy push parity is a separate,
unselected scope.

See the [owned-loopback demo](../samples/PushNotificationReceiver/README.md) and
the [security guide](security.md).
