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
