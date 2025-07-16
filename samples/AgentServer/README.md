# AgentServer

A sample ASP.NET Core application demonstrating the A2A (Agent-to-Agent) framework with different agent implementations.

## Available Agents

The AgentServer supports three different agent types:

- **echo**: A simple echo agent that responds with the same message it receives
- **echotasks**: An echo agent with task management capabilities
- **researcher**: A research agent with advanced capabilities

## Running the Application

### Visual Studio

1. Open the solution in Visual Studio
2. Set AgentServer as the startup project
3. In the Debug toolbar, click the dropdown next to the green play button
4. Select one of the available profiles:
   - **echo-agent**: Explicitly runs the echo agent on HTTP (port 5048)
   - **echotasks-agent**: Runs the echo agent with tasks on HTTP (port 5048)
   - **researcher-agent**: Runs the researcher agent on HTTP (port 5048)
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

#### Using the integrated terminal:
```bash
# Navigate to the AgentServer directory
cd samples/AgentServer

# Run with different profiles using dotnet run
dotnet run --launch-profile echo-agent
dotnet run --launch-profile echotasks-agent
dotnet run --launch-profile researcher-agent
```

#### Using command line arguments directly:
```bash
# Navigate to the AgentServer directory
cd samples/AgentServer

# Run with specific agent types
dotnet run --agent echo
dotnet run --agent echotasks
dotnet run --agent researcher
```

## Endpoints

Each agent is mapped to its respective endpoint:

- Echo agent: `http://localhost:5048/echo`
- Echo with tasks agent: `http://localhost:5048/echotasks`
- Researcher agent: `http://localhost:5048/researcher`

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
- `researcher-agent.http`: Test researcher agent specific features
- `task-management.http`: Test task management features