# AgentClient

A collection of samples demonstrating how to interact with A2A (Agent-to-Agent) framework agents using the A2A .NET client library.

## Overview

The AgentClient samples show how to communicate with A2A agents in different scenarios:

- **Agent Discovery**: Retrieving agent capabilities and metadata
- **Message-based Communication**: Direct, stateless messaging with immediate responses  
- **Task-based Communication**: Creating and managing persistent agent tasks

## Available Samples

### 1. GetAgentDetailsSample

**Purpose**: Demonstrates how to discover and retrieve agent capabilities using the agent card.

**What it shows**:
- Using `A2ACardResolver` to retrieve agent metadata
- Accessing agent capabilities, supported modalities, and skills

**Key APIs**:
- `A2ACardResolver.GetAgentCardAsync()`
- `AgentCard` properties and metadata

### 2. MessageBasedCommunicationSample

**Purpose**: Shows stateless, immediate communication with agents - perfect for simple chat-style interactions.

**What it shows**:
- Sending messages directly to agents
- Both streaming and non-streaming communication
- Immediate responses without task management
- Simple query-response patterns

**Key APIs**:
- `A2AClient.SendMessageAsync()` (non-streaming)
- `A2AClient.SendMessageStreamAsync()` (streaming with Server-Sent Events)
- `Message` class for user messages

### 3. TaskBasedCommunicationSample

**Purpose**: Demonstrates creating and managing persistent agent tasks.

**What it shows**:
- Creating agent tasks
- Handling both short-lived and long-running tasks

**Key APIs**:
- `A2AClient.SendMessageAsync()` (returns `AgentTask`)
- `A2AClient.GetTaskAsync()`
- `A2AClient.CancelTaskAsync`
- `Message` class for user messages
- `AgentTask` class for agent tasks

## Code Structure

```
samples/AgentClient/
├── Program.cs                              # Main entry point
├── AgentServerUtils.cs                     # Utility for managing agent servers
├── Samples/
│   ├── GetAgentDetailsSample.cs           # Agent discovery sample
│   ├── MessageBasedCommunicationSample.cs # Direct messaging sample
│   └── TaskBasedCommunicationSample.cs    # Task management sample
└── README.md                              # This file
```

## Building and Running

### Prerequisites

- .NET 9 SDK or later
- Visual Studio 2022 or VS Code (optional)

### Command Line

```bash
# Build the project
cd samples/AgentClient
dotnet build

# Run all samples
dotnet run
```

### Visual Studio

1. Open the solution in Visual Studio
2. Set `AgentClient` as the startup project
3. Press F5 or click the green play button

### VS Code

1. Open the workspace in VS Code
2. Open the Debug panel (Ctrl+Shift+D)
3. Press F5 to start debugging

### Running Individual Samples

To run individual samples, modify the `Program.cs` file to comment out the samples you don't want to run.