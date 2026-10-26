# AgentServer

A sample ASP.NET Core application demonstrating the A2A (Agent-to-Agent) framework with different agent implementations.

## Available Agents

The AgentServer supports these agent types:

- **echo**: A simple echo agent that responds with the same message it receives
- **echotasks**: An echo agent with task management capabilities
- **researcher**: A research agent with advanced capabilities
- **speccompliance**: A specification compliance agent used to test compliance with the A2A specification
- **streamingartifact**: Streams an artifact incrementally
- **proactive**: Keeps a task Working and sends externally triggered messages through push notifications

## Running the Application

### Visual Studio

1. Open the solution in Visual Studio
2. Set AgentServer as the startup project
3. In the Debug toolbar, click the dropdown next to the green play button
4. Select one of the available profiles:
   - **echo-agent**: Explicitly runs the echo agent on HTTP (port 5048)
   - **echotasks-agent**: Runs the echo agent with tasks on HTTP (port 5048)
   - **researcher-agent**: Runs the researcher agent on HTTP (port 5048)
   - **speccompliance-agent**: Runs the specification compliance agent on HTTP (port 5048)
5. Press F5 or click the green play button to start debugging

### VS Code

#### Using the Debug Panel:
1. Open the workspace in VS Code
2. Open the Debug panel by pressing **Ctrl+Shift+D** or clicking the Run and Debug icon in the sidebar
3. Click the green play button or press **F5** to start debugging
4. A dropdown will appear at the top of VS Code - click on **"More C# options"**
5. Select one of the available profiles:
   - **echo-agent**: Runs the echo agent on HTTP (port 5048)
   - **echotasks-agent**: Runs the echo agent with tasks on HTTP (port 5048)
   - **researcher-agent**: Runs the researcher agent on HTTP (port 5048)
   - **speccompliance-agent**: Runs the specification compliance agent on HTTP (port 5048)

#### Using the integrated terminal:
```bash
# Navigate to the AgentServer directory
cd samples/AgentServer

# Run with different profiles using dotnet run
dotnet run --launch-profile echo-agent
dotnet run --launch-profile echotasks-agent
dotnet run --launch-profile researcher-agent
dotnet run --launch-profile speccompliance-agent
```

#### Using command line arguments directly:
```bash
# Navigate to the AgentServer directory
cd samples/AgentServer

# Run with specific agent types
dotnet run --agent echo
dotnet run --agent echotasks
dotnet run --agent researcher
dotnet run --agent speccompliance
```

## Endpoints

Each agent is mapped to its respective endpoint:

- Echo agent: `http://localhost:5048/echo`
- Echo with tasks agent: `http://localhost:5048/echotasks`
- Researcher agent: `http://localhost:5048/researcher`
- Specification compliance agent: `http://localhost:5048/speccompliance`
- Streaming artifact agent: `http://localhost:5048/streamingartifact`
- Proactive agent: `http://127.0.0.1:5048/proactive`

## Proactive task continuation with A2ACli

This sample demonstrates `A2AServer.ContinueTaskAsync`: an external HTTP
trigger continues an existing task and emits an agent `Message` containing
the exact supplied text, without inventing another inbound user message.
The task stays **Working**, so the trigger can be called repeatedly.

Run the following commands from the repository root, using the .NET 10 SDK.

### Terminal 1: start the agent

```powershell
dotnet run --project samples\AgentServer --no-launch-profile -- `
  --agent proactive --urls http://127.0.0.1:5048 `
  --push-callback http://127.0.0.1:5000/notify
```

The callback policy is enabled only for this agent mode. It allows exactly
the configured HTTP callback at `127.0.0.1`, including its port and path;
the normal production push URL policy remains unchanged for other modes.

### Terminal 2: start A2ACli with push notifications

```powershell
dotnet run --project samples\A2ACli -f net10.0 -- `
  --agent http://127.0.0.1:5048 `
  --return-immediately --use-push-notifications `
  --push-notification-receiver http://127.0.0.1:5000
```

Enter a message such as `Start an external job`, then press Enter at the
file-attachment prompt to skip it. Copy the task's `id` from the returned
JSON and leave the CLI running at its next prompt.

`--return-immediately` sets `SendMessageConfiguration.ReturnImmediately`
to `true`, allowing the initial response to contain a Working task.
Without it, the CLI sends a blocking request, which must end in a terminal
or interrupted state; merely enabling push notifications does not make a
request nonblocking.

### Terminal 3: trigger a continuation

```powershell
$taskId = '<id copied from the CLI>'
Invoke-RestMethod `
  -Uri "http://127.0.0.1:5048/proactive/tasks/$taskId/continue" `
  -Method Post -ContentType 'application/json' `
  -Body (@{ message = 'External job finished' } | ConvertTo-Json)
```

The response includes the task and context IDs, `persistedEventCount: 1`,
and `acceptedPushNotificationCount: 1`. In terminal 2, the CLI prints a
`Push notification => {"message":...}` event with the same task/context
IDs, role `ROLE_AGENT`, and text `External job finished`. Call the trigger
again with different text to send another message to the same task.

The endpoint resolves the same stock `A2AServer` used by the A2A routes.
Persistence and push admission go through `ContinueTaskAsync`; the trigger
does not send directly to the callback. An accepted push count means the
sender accepted delivery work, not that the receiver acknowledged it.
The CLI's printed notification demonstrates actual receipt.

Empty messages return HTTP 400, unknown tasks return 404, and terminal
tasks or tasks without a push registration return 409. Continuation
failures include `failureKind` and committed event/push counts; previously
persisted progress is not rolled back.

**Local demonstration only:** the trigger has no production authentication
and accepts only loopback callers. Bind the server to `127.0.0.1` as shown.
Production triggers need application-specific authentication, authorization,
and task ownership checks; do not copy this callback policy into production.
Tasks and registrations are in memory by default and disappear on restart.

The equivalent HTTP requests are in
[`http-tests/proactive.http`](http-tests/proactive.http). To run an automated
end-to-end check that starts the agent and real CLI receiver, run:

```powershell
pwsh -File .\tests\ProactiveSample.Tests.ps1
```

The script builds both samples with `--no-restore`; restore their projects
first on a fresh checkout. It checks real push receipt, repeated triggers,
Working state, preserved history, and error responses, then stops its
owned processes.

## Testing

**Prerequisite**: Make sure the AgentServer application is running before executing any HTTP tests (see [Running the Application](#running-the-application) section above).

The `http-tests` directory contains HTTP test files that can be executed directly in both Visual Studio and VS Code:

### Visual Studio
- Open any `.http` file in Visual Studio
- Click the green "Send Request" button next to each HTTP request
- View the response in the output window

### VS Code
- Install the REST Client extension
- Open any `.http` file
- Click "Send Request" above each HTTP request
- View the response in a new tab

### Available test files:
- `agent-card.http`: Test agent card functionality
- `message-send.http`: Test message sending
- `message-stream.http`: Test message streaming
- `push-notifications.http`: Test push notifications
- `proactive.http`: Trigger a proactive message and inspect the existing task
- `researcher-agent.http`: Test researcher agent specific features
- `task-management.http`: Test task management features

## A2A Specification Compliance Verification

The `speccompliance` agent is specifically designed to validate compliance with the A2A specification. You can verify this compliance using the official A2A TCK.

### Prerequisites

1. **Start the speccompliance agent**: Make sure the AgentServer is running with the speccompliance agent (see [Running the Application](#running-the-application) above)
2. **Install TCK prerequisites**: Follow the [A2A TCK requirements](https://github.com/a2aproject/a2a-tck#requirements) for your platform

### Running the TCK

1. **Clone the A2A TCK repository**:
   ```bash
   git clone https://github.com/a2aproject/a2a-tck.git
   cd a2a-tck
   ```

2. **Setup Python environment**:
   ```bash
   # Create virtual environment
   uv venv
   
   # Activate virtual environment
   # On Windows:
   .venv\Scripts\activate
   # On macOS/Linux:
   source .venv/bin/activate
   
   # Install dependencies
   uv pip install -e .
   ```

3. **Run the compliance tests** against the speccompliance agent:
   ```bash
   python ./run_tck.py --sut-url http://localhost:5048/speccompliance --category all
   ```

The TCK will run a comprehensive suite of tests to verify that the agent correctly implements the A2A specification, including:

- Agent card discovery and metadata validation
- Message handling and response formatting
- Task management capabilities
- Error handling and status codes
- Protocol compliance for all supported endpoints

For detailed information about the A2A specification and compliance requirements, visit the [A2A Project repository](https://github.com/a2aproject/A2A).