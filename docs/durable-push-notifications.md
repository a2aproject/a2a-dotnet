# Implementing a durable push notification sender

`IPushNotificationSender` is the v1 SDK extension point for accepting notification
work independently of agent execution. Replace the default
`HttpPushNotificationSender` when accepted notifications must survive process
failure, need persistent retry schedules, or require dead-letter handling.
The SDK does not supply a durable sender or broker integration.

This guide describes implementation responsibilities, not additional guarantees
provided by the stock server. See the [push notification guide](push-notifications.md)
for configuration, protocol behavior, and the default sender's limits.

## Choose the guarantee

| Requirement | Implementation responsibility |
|---|---|
| Accepted notifications survive restart | Persist work before successful enqueue completion; recover it after restart |
| Retry transient failures across restarts | Persist attempt state, next-attempt time, and recovery/lease state |
| Inspect and recover exhausted deliveries | Provide protected dead-letter storage and authorized replay |
| Every committed task update has a durable notification record | Integrate task persistence and outbox publication in one transaction |
| No duplicate receiver-side effects | Use receiver-side idempotency and durable deduplication; a sender alone cannot ensure exactly-once effects |

Durable acceptance is not remote acknowledgment. A database commit or broker
confirmation proves only the durability promised by that system's configuration.
Document replication, retention, expiration, eviction, and disaster-recovery
limits rather than promising indefinite delivery.

## Register a replacement

Register your implementation before `AddA2AAgent`, which uses `TryAddSingleton`
for the sender:

```csharp
builder.Services.AddSingleton<IPushNotificationSender, MyDurablePushNotificationSender>();
builder.Services.AddA2AAgent<MyAgent>(card);
```

`MyDurablePushNotificationSender` is an application-defined implementation, not
an SDK type. Configure its database/broker, registration store, HTTP transport,
and delivery policy separately. `PushNotificationDeliveryOptions` controls the
stock sender; it does not automatically configure a replacement.

The SDK hosting adapter starts and stops the selected sender if it also
implements `IHostedService`; it does not start a second stock sender. Do not
also register that same instance as a hosted service, which would give it two
lifecycle owners. A producer that only writes to a broker can instead use a
separately hosted delivery worker. Directly composed senders/workers need
explicit lifecycle and disposal ownership.

## Implement durable admission

The interface accepts one immutable registration generation and one event:

```csharp
Task EnqueueAsync(
    PushNotificationConfigSnapshot registration,
    StreamResponse response,
    CancellationToken cancellationToken = default);
```

Before returning successfully:

1. Validate that the response contains exactly one payload and belongs to the
   registered task. Observe admission cancellation and registration removal.
2. Snapshot the response; do not retain the caller's mutable object. Use the
   source-generated `StreamResponse` metadata from `A2AJsonUtilities.DefaultOptions`
   when serializing A2A payloads.
3. Record the authorized scope, task/configuration IDs, registration `Version`,
   immutable payload, and enough destination/credential information to recover
   the same generation. Assign a delivery ID that remains stable across retries.
4. Commit the work to durable storage or obtain the broker's durable publisher
   confirmation. Only then report successful acceptance.

Bound admission time, storage usage, and concurrency. Use asynchronous I/O
rather than blocking the calling thread. If capacity or persistence fails,
surface a secret-safe error instead of dropping work, returning success, or
silently falling back to memory.

Cancellation is not proof that a remote commit failed: the store or broker may
have accepted work before its acknowledgment was lost. Design for this ambiguous
outcome. The interface does not supply a universal event ID or transaction ID,
so do not assume repeated calls can always be deduplicated. An ID assigned by
the sender stabilizes its own retries, not necessarily a new enqueue of the
same logical event. Do not deduplicate solely by task ID or payload hash;
multiple legitimate events can share them.

## Recover and deliver accepted work

A typical worker claims a due record, checks its registration, makes a bounded
HTTP attempt, and durably records success, retry scheduling, or a terminal
outcome. Acknowledge broker work only after recording the applicable outcome
or durably handing it off for retry/dead-letter processing.

Use leases or equivalent broker ownership to coordinate multiple workers.
Expired leases must allow recovery after worker failure. Bound prefetch,
concurrency, attempt duration, and total retry age. Avoid holding database
transactions open during HTTP calls or backoff delays.

HTTP 2xx acknowledges receipt. If the receiver processes a request but its
response is lost, or the worker crashes before saving success, recovery can
send the notification again. Receivers must handle duplicates. If you expose
a delivery ID for deduplication, agree its transport and meaning with the
receiver; the interface does not define a standard delivery-ID header.

### Retries and dead letters

Define which failures are transient, exponential backoff with a cap and jitter,
attempt/age limits, and handling of `Retry-After` where applicable. The stock
sender retries network failures, timeouts, 408, 429, and 5xx; it does not retry
other 4xx or redirects. A replacement owns its policy and must document any
differences.

Persist retry schedules so a restart does not reset limits or create a retry
storm. Keep authentication failures distinct from connectivity failures.
HTTP 401/403 often require credential or authorization changes, not indefinite
retries. Do not interpret every 403 as token expiry.

Dead-letter records should include a secret-safe reason, attempt counts, and
timestamps. Restrict operator access, audit replay, enforce retention, and
revalidate registration and destination before each replay. Dead-lettering
does not mean the receiver definitely failed to process the request.

### Ordering and fan-out

The stock server orders enqueue calls per task within its own process. A
multi-worker sender or broker does not automatically preserve delivery order:
partition appropriately and define whether a delayed retry blocks subsequent
events for a task/registration. There is no built-in cross-process sequencing
contract.

Track each event/registration pair independently. Fan-out can partially
succeed: accepting one registration's work does not make another registration's
failed admission successful, and retrying a whole batch may duplicate earlier
deliveries.

## Preserve deletion and credential boundaries

Persist the registration generation, not just task/configuration IDs. Before
every attempt, including recovery and administrative replay, query the
authoritative `IPushNotificationStore` in the same authorized scope and verify
the generation still exists and matches. Do not substitute the current
registration when an ID has been deleted and recreated.

`CancellationToken` values are process-local and cannot be serialized for
restart recovery. Reconnect to the store's current generation-scoped removal
signal and observe it during attempts and retry waits. Distributed stores must
coordinate invalidation across delivery processes; a row deletion or eventual
cache expiration alone does not cancel in-flight work. A store outage is not
proof that a registration is absent: defer or surface failure without sending
unchecked work.

Removed generations must not be delivered or resurrected by replay. Deletion
can suppress future work and cancel requests, but cannot recall transmitted
bytes or undo receiver processing. The built-in HTTP sender deletes terminal
registrations after final delivery handling by default. Replacement senders must
define and document their own retention policy; `AutoDeleteOnTerminalState` does
not apply to them. If performing terminal cleanup, preserve pending delivery and
retries, and delete only the delivered registration generation.

Durable work and dead letters can contain credentials as well as sensitive task
payloads. Encrypt and restrict access to records, broker messages, backups, and
diagnostics. A reference-based design must resolve the exact registration/secret
version, not silently use the latest credential or destination. An in-memory
registration store alone cannot recover registrations after a restart; pair
the sender with appropriately durable registration storage if delivery is to
resume.

Credential renewal is a separate policy. The stock SDK does not acquire or
refresh tokens, and v1 create is not an update operation. Replacing a registration
does not transfer old-generation deliveries to it. Do not automatically replay
old data to a new URL or identity after rotation. Any application-specific
renewal/replay workflow needs explicit authorization and generation semantics.

## Preserve webhook security and wire behavior

Send the v1 `StreamResponse` payload with the configured authentication in
request headers; the stock sender uses `application/a2a+json`. Set authentication
per request, not on shared default headers. The optional legacy token header
does not replace v1 authentication.

Keep destination validation before each attempt and enforce approved addresses
at connection establishment to prevent unchecked DNS re-resolution. Retain
HTTPS certificate validation, redirect/cookie protections, host allowlists,
and appropriate egress controls. A different transport or remote delivery worker
must implement those protections too; merely resolving the URL validator does
not make an arbitrary HTTP client safe.

Carry trusted tenant/principal routing information for background work rather
than retaining `HttpContext` or trusting a client-supplied `Tenant`. Protect
administrative retry/cancel operations with the same ownership checks.
Redact credentials, payloads, and sensitive URLs from logs, tracing, and error
messages. See the [security guide](security.md#5-webhook--push-notification-security).

## Understand the task-persistence crash window

With the stock server, task persistence and sender admission are separate
operations:

```text
Persist task update -> EnqueueAsync -> Commit durable notification work
```

A crash after the task commit but before durable admission can leave a committed
update without a notification. Returning an admission error does not roll back
the task. A durable sender addresses loss after acceptance; injecting one alone
does not close this earlier window.

For atomic publication, integrate task persistence and an outbox write in the
same database transaction, then let a recoverable dispatcher perform delivery
or publish to a broker. The outbox must capture the intended immutable event
and registration generations, not reconstruct intermediate events from the
latest task state. A broker publish after the database commit is still a
separate operation.

The `IPushNotificationSender` method has no transaction parameter and the stock
server does not provide this shared transaction. Applications needing it must
design the task-store/publication integration, potentially using a custom
request handler. Do not claim atomic publication just because both services
use the same database. `GetTask` can reconcile current task state and available
artifacts/history, but is not replay of every missed event.

## Operate and verify the sender

Monitor durable acceptance failures, queue depth/oldest age, retry counts,
authentication failures, dead-letter growth, and recovery lag without exposing
secrets. Stop new admission deliberately during shutdown; bound active work
and persist/release claims so another worker can resume. A host deadline must
not silently erase already-accepted records.

Exercise crashes before/after durable admission, after receiver processing,
and before recording success. Test ambiguous publisher confirmations, lease
expiry, multiple workers, partial fan-out, ordering under retries, and
capacity limits. Include generation deletion/recreation during admission,
backoff and HTTP requests; restart with removed registrations; store/broker
outages; credential expiry; and unauthorized replay. Verify destination
security and secret-safe diagnostics in the actual worker environment.
