# A2A client

This console client discovers and invokes the travel agent using the A2A
protocol and Microsoft Agent Framework's `AIAgent` abstraction.

The client:

1. Resolves the agent card with `A2ACardResolver`.
2. Creates an `AIAgent` adapter for the remote A2A agent.
3. Creates one `AgentSession` and reuses it across console messages.
4. Invokes the remote agent with `RunAsync`.

## Run the sample

Start the server in a separate terminal:

```powershell
$env:OPENAI_API_KEY="<your-api-key>"
$env:OPENAI_CHAT_MODEL_NAME="gpt-5.4-mini"
cd samples\MAF\A2AServer
dotnet run
```

Keep the server running, then start the client:

```powershell
cd samples\MAF\A2AClient
dotnet run
```

Ask:

```text
Show me the available tour in Japan and the current local time in Asia/Tokyo.
```

Then continue the same session:

```text
Now compare it with the available tour in Ireland and the time in Europe/Dublin.
```

Enter `:q` or `quit` to stop the client.

## Connect to another endpoint

The client discovers `http://localhost:5000` by default. To use another
address, start the server with matching listening and advertised URLs:

```powershell
$env:ASPNETCORE_URLS="http://localhost:6000"
$env:A2A_AGENT_URL="http://localhost:6000"
dotnet run
```

Then set the discovery URL before starting the client:

```powershell
$env:A2A_AGENT_URL="http://localhost:6000"
dotnet run
```

## Test the server with the HTTP file

With the server running, open `..\A2AServer\A2AServer.http` in an editor that
supports HTTP files, such as Visual Studio or Visual Studio Code with an HTTP
client extension. The file includes requests to:

1. Retrieve the agent card.
2. Invoke the agent through JSON-RPC.
3. Invoke the agent through HTTP+JSON.

The file targets `http://localhost:5000` by default. Update its `@host` variable
if the server listens at another address.

## Inspect the server with A2A Inspector

Follow the
[A2A Inspector setup instructions](https://github.com/a2aproject/a2a-inspector)
and connect it to the running server at `http://localhost:5000`.

The Inspector provides another A2A client for viewing the agent card and
sending messages without running this console client.
