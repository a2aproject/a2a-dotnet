# A2A .NET SDK

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![NuGet Version](https://img.shields.io/nuget/v/A2A.svg)](https://www.nuget.org/packages/A2A/)

A .NET library that helps run agentic applications as A2AServers following the [Agent2Agent (A2A) Protocol](https://a2a-protocol.org).

The A2A .NET SDK provides a robust implementation of the Agent2Agent (A2A) protocol, enabling seamless communication between AI agents and applications. This library offers both high-level abstractions and fine-grained control, making it easy to build A2A-compatible agents while maintaining flexibility for advanced use cases.

Key features include:
- **Agent Capability Discovery**: Retrieve agent capabilities and metadata through agent cards
- **Message-based Communication**: Direct, stateless messaging with immediate responses
- **Task-based Communication**: Create and manage persistent, long-running agent tasks
- **Streaming Support**: Real-time communication using Server-Sent Events
- **ASP.NET Core Integration**: Built-in extensions for hosting A2A agents in web applications
- **Cross-platform Compatibility**: Supports .NET 8+

## Protocol Compatibility

This library implements the [A2A v1.0 specification](https://a2a-protocol.org). It provides full support for the JSON-RPC binding, the HTTP+JSON REST binding (including streaming via Server-Sent Events), and the gRPC binding (including server-streaming). All three bindings share the same `IA2AClient` contract and `IA2ARequestHandler` server pipeline.

If you're upgrading from the v0.3 SDK, see the **[Migration Guide](docs/migration-guide-v1.md)** for a comprehensive list of breaking changes and before/after code examples. A backward-compatible `A2A.V0_3` NuGet package is available during the transition:

```bash
dotnet add package A2A.V0_3
```

## Installation

### Core A2A Library

```bash
dotnet add package A2A
```

### ASP.NET Core Extensions

```bash
dotnet add package A2A.AspNetCore
```

### gRPC Binding

The gRPC binding ships as two optional packages so core consumers are not forced to depend on `Grpc.*`. Add the client package for a gRPC `IA2AClient`, and the ASP.NET Core package to host a gRPC server:

```bash
dotnet add package A2A.Grpc            # gRPC client + protocol mapping
dotnet add package A2A.Grpc.AspNetCore # gRPC server host (MapGrpcA2A)
```

## Overview
![alt text](https://github.com/a2aproject/a2a-dotnet/raw/main/overview.png)

## Library: A2A
This library contains the core A2A protocol implementation. It includes the following key classes:

### Client Classes
- **`A2AClient`**: Primary client for making A2A requests to agents. Supports both streaming and non-streaming communication, task management, and push notifications.
- **`A2ACardResolver`**: Resolves agent card information from A2A-compatible endpoints to discover agent capabilities and metadata.

### Server Classes  
- **`A2AServer`**: Core server that handles A2A JSON-RPC requests, manages task lifecycle via `TaskProjection`, and coordinates streaming/non-streaming responses. Implements `IA2ARequestHandler`.
- **`IAgentHandler`**: Interface that agents implement. Provides `ExecuteAsync()` for message handling and `CancelAsync()` for task cancellation.
- **`TaskUpdater`**: Convenience API for emitting task lifecycle events (Submit, StartWork, AddArtifact, Complete, Fail, Cancel, RequireInput).
- **`MessageResponder`**: Convenience API for stateless message replies without task lifecycle.
- **`RequestContext`**: Pre-resolved context provided to agents, containing the incoming message, task ID, context ID, and helper properties like `UserText` and `IsContinuation`.
- **`AgentEventQueue`**: Channel-backed queue that agents write events to. The SDK reads from it to build responses.
- **`ITaskStore`**: Interface for task persistence with simple CRUD methods (Get, Save, Delete, List).
- **`InMemoryTaskStore`**: In-memory implementation of `ITaskStore` suitable for development and testing.

### Core Models
- **`AgentTask`**: Represents a task with its status, history, artifacts, and metadata.
- **`AgentCard`**: Contains agent metadata, capabilities, and endpoint information.
- **`Message`**: Represents messages exchanged between agents and clients.

## Library: A2A.AspNetCore
This library provides ASP.NET Core integration for hosting A2A agents. It includes the following key classes:

### Extension Methods
- **`A2AServiceCollectionExtensions`**: Provides `AddA2AAgent<THandler>()` for registering an agent, its card, and all A2A services with dependency injection.
- **`A2ARouteBuilderExtensions`**: Provides `MapA2A()` for JSON-RPC endpoints, `MapHttpA2A()` for HTTP REST endpoints, and `MapWellKnownAgentCard()` for agent card discovery.

## Library: A2A.Grpc and A2A.Grpc.AspNetCore
These optional libraries add the gRPC binding on top of the same client contract and server pipeline.

### A2A.Grpc (client)
- **`A2AGrpcClient`**: An `IA2AClient` implementation over gRPC with parity for all unary and server-streaming operations. gRPC faults are surfaced as `A2AException` with the correct `A2AErrorCode`.
- **`A2AGrpcClientRegistration.Register()`**: Registers the `GRPC` binding with `A2AClientFactory`, so `A2AClientFactory.Create(agentCard)` resolves a gRPC client for agent interfaces advertising `GRPC`.

### A2A.Grpc.AspNetCore (server)
- **`GrpcA2ARouteBuilderExtensions`**: Provides `AddA2AGrpc()` to register gRPC services and `MapGrpcA2A()` to map the A2A gRPC service onto the shared `IA2ARequestHandler` — mirroring `MapA2A` / `MapHttpA2A`.

## Getting Started

### 1. Create an Agent Server

```csharp
using A2A;
using A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Register the echo agent with DI — this sets up A2AServer, ITaskStore, and all middleware
builder.Services.AddA2AAgent<EchoAgent>(EchoAgent.GetAgentCard("http://localhost:5000/echo"));

var app = builder.Build();

// Map JSON-RPC endpoint for A2A communication
app.MapA2A("/echo");

// Map well-known agent card for discovery
var card = app.Services.GetRequiredService<AgentCard>();
app.MapWellKnownAgentCard(card);

app.Run();

// Define the agent — implement IAgentHandler
public sealed class EchoAgent : IAgentHandler
{
    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        var responder = new MessageResponder(eventQueue, context.ContextId);
        await responder.ReplyAsync($"Echo: {context.UserText}", cancellationToken);
    }

    public static AgentCard GetAgentCard(string url) => new()
    {
        Name = "Echo Agent",
        Description = "Echoes messages back to the user",
        Version = "1.0.0",
        SupportedInterfaces = [new AgentInterface
        {
            Url = url,
            ProtocolBinding = "JSONRPC",
            ProtocolVersion = "1.0"
        }],
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Capabilities = new AgentCapabilities { Streaming = false },
        Skills = [new AgentSkill
        {
            Id = "echo",
            Name = "Echo",
            Description = "Echoes back user messages",
            Tags = ["echo"]
        }],
    };
}
```

### 2. Connect with A2AClient

```csharp
using A2A;

// Discover agent
var cardResolver = new A2ACardResolver(new Uri("http://localhost:5000/"));
var agentCard = await cardResolver.GetAgentCardAsync();

// Create client using agent's endpoint
var client = new A2AClient(new Uri(agentCard.SupportedInterfaces[0].Url));

// Send message
var response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.User,
        Parts = [Part.FromText("Hello!")]
    }
});

// Handle response
switch (response.PayloadCase)
{
    case SendMessageResponseCase.Message:
        Console.WriteLine(response.Message!.Parts[0].Text);
        break;
    case SendMessageResponseCase.Task:
        Console.WriteLine($"Task created: {response.Task!.Id}");
        break;
}
```

### 3. Using the gRPC binding

Host a gRPC endpoint alongside (or instead of) the JSON-RPC/HTTP endpoints. The gRPC service reuses the same agent registration and `IA2ARequestHandler` pipeline:

```csharp
using A2A;
using A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddA2AAgent<EchoAgent>(EchoAgent.GetAgentCard("https://localhost:5001"));
builder.Services.AddA2AGrpc(); // registers ASP.NET Core gRPC services

var app = builder.Build();
app.MapGrpcA2A(); // maps the A2A gRPC service
app.Run();
```

Connect with the gRPC client — either directly or via `A2AClientFactory` when the agent card advertises a `GRPC` interface:

```csharp
using A2A;
using A2A.Grpc;

// Direct construction
using var client = new A2AGrpcClient(new Uri("https://localhost:5001"));

// Or resolve from the agent card by binding preference
A2AGrpcClientRegistration.Register();
var resolved = A2AClientFactory.Create(
    agentCard,
    options: new A2AClientOptions { PreferredBindings = [ProtocolBindingNames.Grpc] });

await foreach (var evt in client.SendStreamingMessageAsync(new SendMessageRequest
{
    Message = new Message
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.User,
        Parts = [Part.FromText("Hello over gRPC!")]
    }
}))
{
    Console.WriteLine(evt.PayloadCase);
}
```

## Samples

The repository includes several sample projects demonstrating different aspects of the A2A protocol implementation. Each sample includes its own README with detailed setup and usage instructions.

### Agent Client Samples
**[`samples/AgentClient/`](samples/AgentClient/README.md)**

Comprehensive collection of client-side samples showing how to interact with A2A agents:
- **Agent Capability Discovery**: Retrieve agent capabilities and metadata using agent cards
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

## Further Reading

To learn more about the A2A protocol, explore these additional resources:

- **[A2A Protocol Documentation](https://a2a-protocol.org/latest/)** - The official documentation for the A2A protocol.
- **[A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)** - The detailed technical specification of the protocol.
- **[A2A Topics](https://a2a-protocol.org/latest/topics/what-is-a2a/)** - An overview of key concepts and features of the A2A protocol.
- **[A2A Roadmap](https://a2a-protocol.org/latest/roadmap/)** - A look at the future development plans and upcoming features.

## Acknowledgements

This library builds upon [Darrel Miller's](https://github.com/darrelmiller) [sharpa2a](https://github.com/darrelmiller/sharpa2a) project. Thanks to Darrel and all the other contributors for the foundational work that helped shape this SDK.

## License

This project is licensed under the [Apache 2.0 License](LICENSE).

