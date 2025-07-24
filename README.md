# A2A: A .NET implementation of the Google A2A protocol
Interact with agents using the A2A protocol in .NET applications. This library is designed to be used with ASP.NET Core applications and provides a simple way to add A2A support to your agents.

## Overview
![alt text](overview.png)

## Library: A2A
This library contains the core A2A protocol implementation. It includes the following key classes:

### Client Classes
- **`A2AClient`**: Primary client for making A2A requests to agents. Supports both streaming and non-streaming communication, task management, and push notifications.
- **`A2ACardResolver`**: Resolves agent card information from A2A-compatible endpoints to discover agent capabilities and metadata.

### Server Classes  
- **`TaskManager`**: Manages the complete lifecycle of agent tasks including creation, updates, cancellation, and event streaming. Handles both message-based and task-based communication patterns.
- **`ITaskStore`**: An interface for abstracting the storage of tasks.
- **`InMemoryTaskStore`**: Simple in-memory implementation of `ITaskStore` suitable for development and testing scenarios.

### Core Models
- **`AgentTask`**: Represents a task with its status, history, artifacts, and metadata.
- **`AgentCard`**: Contains agent metadata, capabilities, and endpoint information.
- **`Message`**: Represents messages exchanged between agents and clients.

## Library: A2A.AspNetCore
This library provides ASP.NET Core integration for hosting A2A agents. It includes the following key classes:

### Extension Methods
- **`A2ARouteBuilderExtensions`**: Provides `MapA2A()` and `MapHttpA2A()` extension methods for configuring A2A endpoints in ASP.NET Core applications.

## Getting Started

### 1. Create an Agent Server

```csharp
using A2A;
using A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Create and register your agent
var taskManager = new TaskManager();
var agent = new EchoAgent();
agent.Attach(taskManager);

app.MapA2A(taskManager, "/echo");
app.Run();

public class EchoAgent
{
    public void Attach(ITaskManager taskManager)
    {
        taskManager.OnMessageReceived = ProcessMessageAsync;
        taskManager.OnAgentCardQuery = GetAgentCardAsync;
    }

    private Task<Message> ProcessMessageAsync(MessageSendParams messageSendParams, CancellationToken cancellationToken)
    {
        var text = messageSendParams.Message.Parts.OfType<TextPart>().First().Text;
        return Task.FromResult(new Message
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = messageSendParams.Message.ContextId,
            Parts = [new TextPart { Text = $"Echo: {text}" }]
        });
    }

    private Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AgentCard
        {
            Name = "Echo Agent",
            Description = "Echoes messages back to the user",
            Url = agentUrl,
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = new AgentCapabilities { Streaming = true }
        });
    }
}
```

### 2. Connect with A2AClient

```csharp
using A2A;

// Discover agent and create client
var cardResolver = new A2ACardResolver(new Uri("http://localhost:5100/"));
var agentCard = await cardResolver.GetAgentCardAsync();
var client = new A2AClient(new Uri(agentCard.Url));

// Send message
var response = await client.SendMessageAsync(new MessageSendParams
{
    Message = new Message
    {
        Role = MessageRole.User,
        Parts = [new TextPart { Text = "Hello!" }]
    }
});
```

## Samples

The repository includes several sample projects demonstrating different aspects of the A2A protocol implementation. Each sample includes its own README with detailed setup and usage instructions.

### Agent Client Samples
**[`samples/AgentClient/`](samples/AgentClient/README.md)**

Comprehensive collection of client-side samples showing how to interact with A2A agents:
- **Agent Discovery**: Retrieve agent capabilities and metadata using agent cards
- **Message-based Communication**: Direct, stateless messaging with immediate responses
- **Task-based Communication**: Create and manage persistent agent tasks
- **Streaming Communication**: Real-time communication using Server-Sent Events

### Agent Server Samples
**[`samples/AgentServer/`](samples/AgentServer/README.md)**

Server-side examples demonstrating how to build A2A-compatible agents:
- **Echo Agent**: Simple agent that echoes messages back to clients
- **Echo Agent with Tasks**: Task-based version of the echo agent
- **Researcher Agent**: More complex agent with research capabilities
- **HTTP Test Suite**: Complete set of HTTP tests for all agent endpoints

### Semantic Kernel Integration
**[`samples/SemanticKernelAgent/`](samples/SemanticKernelAgent/README.md)**

Advanced sample showing integration with Microsoft Semantic Kernel:
- **Travel Planner Agent**: AI-powered travel planning agent
- **Semantic Kernel Integration**: Demonstrates how to wrap Semantic Kernel functionality in A2A protocol

### Command Line Interface
**[`samples/A2ACli/`](samples/A2ACli/)**

Command-line tool for interacting with A2A agents:
- Direct command-line access to A2A agents
- Useful for testing and automation scenarios

### Quick Start with Client Samples

1. **Clone and build the repository**:
   ```bash
   git clone https://github.com/a2aproject/a2a-dotnet.git
   cd a2a-dotnet
   dotnet build
   ```

2. **Run the client samples**:
   ```bash
   cd samples/AgentClient
   dotnet run
   ```

For detailed instructions and advanced scenarios, see the individual README files linked above.

