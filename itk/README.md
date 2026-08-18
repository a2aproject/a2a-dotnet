# Running ITK Tests Locally

This directory contains the .NET ITK (Integration Test Kit) agent and scripts to run
cross-SDK compatibility tests against the A2A .NET SDK.

## What is ITK?

ITK verifies that A2A SDK implementations can interoperate by routing messages through
a cluster of agents built with different SDKs (Python, Go, .NET) across multiple
transport protocols (JSON-RPC, HTTP+JSON, gRPC).

## Prerequisites

- **Docker** (or Podman with docker compatibility)
- **.NET 8.0 SDK** (for building the .NET agent)

## Running Tests

### 1. Set Environment Variable

```bash
export A2A_ITK_REVISION=main
```

### 2. Execute Tests

```bash
cd itk
./run_itk.sh
```

The script will:
1. Clone `a2a-itk` (if not already present)
2. Build the ITK service Docker image (includes Python, Go, .NET runtimes)
3. Mount this repo as the "current" agent under test
4. Run test scenarios and output results

### PR Tests vs Nightly

- **PR tests** (`scenarios.json`): Focused star topology with core behaviors
- **Nightly tests** (`scenarios_full.json`): Full protocol matrix with all behaviors

To run nightly:
```bash
export ITK_NIGHTLY_RUN=TRUE
./run_itk.sh
```

## Debugging

```bash
export ITK_LOG_LEVEL=DEBUG
./run_itk.sh
```

Logs will be saved to `itk/logs/`.

## Architecture

The .NET ITK agent (`ItkAgent.cs`) implements the ITK instruction protocol:

1. **Receives** a protobuf-encoded instruction embedded in an A2A message
2. **Parses** the instruction (CallAgent, ReturnResponse, or SeriesOfSteps)
3. **Executes** the instruction:
   - `CallAgent`: Resolves the target's agent card, creates an A2A client, forwards the nested instruction
   - `ReturnResponse`: Returns the specified text
   - `SeriesOfSteps`: Executes each step sequentially, concatenates results
4. **Returns** the collected trace as the task response

Supported behaviors: `send_message`, `push_notification`, `resubscribe` (streaming with disconnect/reconnect).
