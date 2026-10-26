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

The sample receiver is unauthenticated and only accepts loopback URLs. It is
intended for local development; production webhook receivers should validate
the configured token and authenticate the sending agent.

## Nonblocking tasks

Add `--return-immediately` to set
`SendMessageConfiguration.ReturnImmediately` to `true`. This lets the server
return a task that is still Working instead of waiting for a terminal or
interrupted state. The default remains blocking, even when push notifications
are enabled. Combine this option with `--use-push-notifications` to see later
updates while the CLI waits for your next input.

See the [proactive AgentServer walkthrough](../AgentServer/README.md#proactive-task-continuation-with-a2acli)
for a complete example: create a Working task, call a separate HTTP endpoint
with its task ID and a message, and watch that message arrive through push
notifications.
