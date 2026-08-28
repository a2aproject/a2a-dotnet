# A2A server

This ASP.NET Core server hosts a Microsoft Agent Framework travel agent and
exposes it through the A2A JSON-RPC and HTTP+JSON protocol bindings.

The server demonstrates how to:

1. Create an `AIAgent` from an OpenAI `ResponsesClient`.
2. Register local functions as Agent Framework tools.
3. Preserve conversation context with an in-memory `AgentSessionStore`.
4. Host the agent with `AddA2AServer`.
5. Publish an agent card with streaming capability enabled.

## Tools

| Tool | Purpose |
|---|---|
| `GetAvailableTour` | Reads the available tour for a country from the local catalog |
| `GetCurrentLocalTime` | Reads the host clock and system time-zone database |

`GetAvailableTour` contains sample entries for Ireland, France, Italy, Japan,
Spain, and the United States. `GetCurrentLocalTime` accepts an IANA time-zone ID
such as `Europe/Dublin` or `Asia/Tokyo`. Neither tool makes an external call.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key

## Run the server

Configure your OpenAI API key and, optionally, the model:

```powershell
$env:OPENAI_API_KEY="<your-api-key>"
$env:OPENAI_CHAT_MODEL_NAME="gpt-5.4-mini"
```

Start the server:

```powershell
cd samples\MAF\A2AServer
dotnet run
```

The server must remain running while you use the client, the HTTP requests, or
the A2A Inspector. By default, it listens at `http://localhost:5000`.

## Configuration

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `OPENAI_API_KEY` | Yes | None | Authenticates the OpenAI Responses client |
| `OPENAI_CHAT_MODEL_NAME` | No | `gpt-5.4-mini` | Selects the OpenAI model |
| `ASPNETCORE_URLS` | No | `http://localhost:5000` | Sets the listening address |
| `A2A_AGENT_URL` | No | `http://localhost:5000` | Sets the public URL in the agent card |

Set `ASPNETCORE_URLS` and `A2A_AGENT_URL` together when the server should run at
a different address. The in-memory session store preserves context only while
the server is running.

## Endpoints

| Endpoint | Binding | Purpose |
|---|---|---|
| `GET /.well-known/agent-card.json` | HTTP | Retrieves the agent card |
| `POST /` | JSON-RPC | Sends JSON-RPC A2A requests |
| `POST /message:send` | HTTP+JSON | Sends an A2A message |

## Test with the HTTP file

Open `A2AServer.http` in an editor that supports HTTP files, such as Visual
Studio or Visual Studio Code with an HTTP client extension, and run any request:

1. `Discover the travel agent` retrieves the agent card.
2. `Send a message using JSON-RPC` invokes the JSON-RPC binding.
3. `Send a message using HTTP+JSON` invokes the HTTP+JSON binding.

Start the server before running these requests. If the server uses a different
address, update the `@host` variable at the top of `A2AServer.http`.

## Inspect with A2A Inspector

Follow the
[A2A Inspector setup instructions](https://github.com/a2aproject/a2a-inspector),
start the Inspector, and connect it to `http://localhost:5000`.

The Inspector discovers `/.well-known/agent-card.json`, displays the travel
agent card, and lets you send messages to the running server.
