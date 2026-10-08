# SDK push notification demo and receiver

This sample includes an actual **SDK-to-webhook demo**, plus a standalone
task-specific receiver. It uses owned loopback listeners and fresh local
credentials; no external service is needed.

## Build and run the complete demo

Use a Visual Studio Developer PowerShell with MSBuild 18 and the .NET 10 SDK.
The sample and tests also target .NET 8.

```powershell
MSBuild.exe tests/PushNotificationReceiver.Tests/PushNotificationReceiver.Tests.csproj /restore /p:Configuration=Release
.\samples\PushNotificationReceiver\bin\Release\net10.0\PushNotificationReceiver.exe --demo
```

The demo:

1. Starts an `AddA2AAgent` host with `PushNotifications = true`.
2. Uses `A2AClient` to create a task paused for continuation (`InputRequired`).
3. Starts its own authenticated receiver and registers that exact callback
   through the real create-config operation.
4. Reads the config and verifies credentials are redacted.
5. Continues the SDK task to produce an artifact and completed status.
6. Receives the history-message, artifact, and terminal events through the
   **default `HttpPushNotificationSender`**, not a manually forwarded stream.
7. Drains the sender, verifies the config is retained, explicitly deletes it,
   checks that deletion succeeded, and stops both hosts.

Success prints:

```text
SDK push demo passed: 3 webhook events, task Completed, configuration explicitly deleted.
```

Credentials are generated in memory and never printed. `LocalReceiverUrlValidator`
is an explicit **demo-only** replacement allowing exactly the URL of the listener
this process started. It is not a general HTTP/private-address bypass, and is
never registered by the SDK. Production uses the secure HTTPS/public-address
validator described in the [push guide](../../docs/push-notifications.md).

## Standalone receiver

For a separately configured local sender:

```powershell
$env:A2A_PUSH_TASK_ID = 'local-task'
$env:A2A_PUSH_BEARER_TOKEN = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
.\samples\PushNotificationReceiver\bin\Release\net10.0\PushNotificationReceiver.exe
```

The process prints its assigned `http://127.0.0.1:<port>/notifications` URL and
stops with Ctrl+C. Hosting settings cannot replace its owned IPv4 loopback
binding. Supply only fresh local test credentials, never real production
credentials or checked-in secrets.

| Input | Result |
|---|---|
| Authenticated UTF-8 `application/json` or `application/a2a+json`, known task, exactly one payload | `204`, metadata-only receipt |
| Missing or wrong Bearer credential | `401` |
| Different task ID | `403` |
| Invalid JSON, ambiguous/unwrapped payload, or missing task ID | `400` |
| Unsupported media type or charset | `415` |
| Body above 64 KiB | `413` |

All four v1 payload fields are handled: `task`, `message`, `statusUpdate`, and
`artifactUpdate`. A direct message without a task ID is rejected by this
task-specific receiver. These are receiver policies, not extra SDK acceptance
rules.

Set `A2A_PUSH_NOTIFICATION_TOKEN` only when also requiring the compatibility
`X-A2A-Notification-Token` header. It never substitutes for Bearer authentication.
Repeated terminal notifications receive another `204`; the sample prints
receipts but has no business processing or durable deduplication.

## Tests

```powershell
vstest.console.exe tests/PushNotificationReceiver.Tests/bin/Release/net10.0/PushNotificationReceiver.Tests.dll
vstest.console.exe tests/PushNotificationReceiver.Tests/bin/Release/net8.0/PushNotificationReceiver.Tests.dll
```

Receiver tests cover payloads, auth, task IDs, limits, duplicate receipt, and
binding isolation. Integration tests run real loopback JSON-RPC, HTTP+JSON, gRPC,
against `A2AServer`, its real store, and default HTTP
sender. They cover CRUD/redaction, multiple configs, deletion, inline config,
preserved direct messages, cancellation/disconnection, redirect/failure isolation,
and config retention. They also check that the stock v1 server does not advertise
or accept incomplete v0.3 push support. There is no test-side stream-to-webhook bridge.

The sample is not a production deployment: it has no public TLS endpoint,
tenant authorization, encrypted durable storage, replay protection, or
idempotent business processing. Review the [security guide](../../docs/security.md)
and [delivery limits](../../docs/push-notifications.md) before production use.
