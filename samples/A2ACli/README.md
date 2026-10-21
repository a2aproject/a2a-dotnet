# A2A CLI

The A2A CLI is an interactive client for connecting to an A2A agent.

## Push notifications

Use `--use-push-notifications` to start an in-process callback receiver before sending messages:

```powershell
dotnet run --project samples\A2ACli -- `
  --agent http://localhost:10000 `
  --use-push-notifications `
  --push-notification-receiver http://localhost:5000
```

The CLI listens at `http://localhost:5000/notify`, supplies that URL in the
message's `TaskPushNotificationConfig`, and prints each received
`StreamResponse`.

The receiver only accepts loopback URLs and requires a locally issued, random
Bearer credential. Credentials expire after five minutes. While the CLI is
running, it automatically renews registrations for nonterminal tasks one minute
before expiry, including while waiting for console input. Missing, invalid, and
expired credentials receive HTTP 401.

The server's destination policy must explicitly permit this local callback;
the CLI does not change server-side URL validation.

The CLI learns the server-assigned task ID from the send response or an
authenticated callback, so renewal can also start before a blocking send returns.
It stops renewal when it observes a terminal task state. Direct-message responses
do not need a task registration.

V1 creation is not an update: renewal creates a new configuration ID before
deleting the previous registration. The receiver accepts both credentials until
the old one's original expiry. This avoids deliberately leaving the task without
a registration, but overlapping registrations can cause duplicate notifications;
deleting an old registration also cancels its queued deliveries. It is not a
guarantee of lossless or exactly-once delivery.

Refresh and cleanup failures are reported without printing credentials, and
retried every five seconds with bounded request timeouts. An outage can still
outlast the credential lifetime and interrupt notifications. Renewal and the
receiver stop when the CLI exits; server registrations are not automatically
deleted on exit.

This demonstrates client-managed credential rotation, not OAuth token
acquisition or a protocol-defined refresh handshake. Production clients should
obtain fresh credentials from their identity provider, know their expiry,
persist registration state as needed, and use authenticated HTTPS callbacks.
See [credential lifetime and renewal](../../docs/push-notifications.md#credential-lifetime-and-renewal).
