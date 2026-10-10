# A2A travel agent client and server

This sample demonstrates a minimal end-to-end A2A flow using
[Microsoft Agent Framework](https://github.com/microsoft/agent-framework):

1. `A2AServer` creates a tool-enabled `AIAgent` with the OpenAI Responses API.
2. The server exposes the agent through the A2A JSON-RPC and HTTP+JSON protocol
   bindings and publishes its agent card.
3. `A2AClient` discovers the card, adapts the remote agent to `AIAgent`, and
   sends messages in a shared session.

## Projects

| Project | Description |
|---|---|
| [`A2AServer`](A2AServer/README.md) | Hosts the travel agent and its A2A endpoints |
| [`A2AClient`](A2AClient/README.md) | Discovers and invokes the hosted agent |

The travel agent uses two tools whose data is unavailable to the model:

| Tool | Data source |
|---|---|
| `GetAvailableTour` | The sample's in-memory tour catalog |
| `GetCurrentLocalTime` | The host clock and system time-zone database |

Neither tool makes an external call.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key

## Run the sample

Open two terminals from this directory.

In the first terminal, start the travel agent:

```powershell
$env:OPENAI_API_KEY="<your-api-key>"
$env:OPENAI_CHAT_MODEL_NAME="gpt-5.4-mini"
cd A2AServer
dotnet run
```

In the second terminal, start the client:

```powershell
cd A2AClient
dotnet run
```

Then ask:

```text
Show me the available tour in Japan and the current local time in Asia/Tokyo.
```

Continue the same session with:

```text
Now compare it with the available tour in Ireland and the time in Europe/Dublin.
```

The server listens on `http://localhost:5000` by default.

## Optional configuration

| Variable | Used by | Default | Purpose |
|---|---|---|---|
| `OPENAI_CHAT_MODEL_NAME` | Server | `gpt-5.4-mini` | OpenAI model used by the travel agent |
| `ASPNETCORE_URLS` | Server | `http://localhost:5000` | Address on which the server listens |
| `A2A_AGENT_URL` | Server and client | `http://localhost:5000` | Advertised and discovered A2A endpoint |

Set `ASPNETCORE_URLS` and `A2A_AGENT_URL` together when hosting the server at a
different address.

## Other ways to invoke the server

[`A2AServer/A2AServer.http`](A2AServer/A2AServer.http) contains requests for
discovering the agent card and invoking both protocol bindings. You can also use
the [A2A Inspector](https://github.com/a2aproject/a2a-inspector). Start the
server before using either option.
