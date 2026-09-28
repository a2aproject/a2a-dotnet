# Running ITK Tests Locally

This directory contains the .NET ITK (Interoperability Test Kit) agent and scripts to run
cross-SDK compatibility tests against the A2A .NET SDK.

## What is ITK?

ITK verifies that A2A SDK implementations can interoperate by routing messages through
a cluster of agents built with different SDKs (Python, Go, .NET) across multiple
transport protocols (JSON-RPC, HTTP+JSON, gRPC).

## Prerequisites

- **Docker** (or Podman with docker compatibility)

No local .NET SDK is needed. The agent is built inside the ITK image, which carries the
.NET 10 SDK and the 8.0 ASP.NET Core runtime; `run_itk.sh` removes `publish/` on exit so
a later run cannot exec a stale build.

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

Scenarios come from the shared, role-based sets in a2a-itk rather than from files in
this repo, so adding an SDK or a version line is a change to `a2a-itk/matrix.yaml` and
nothing else:

- **PR** (`a2a-itk/scenarios/traversal/pr.yaml`): a star against a fixed peer set
- **Nightly** (`a2a-itk/scenarios/traversal/nightly.yaml`): every peer in the matrix,
  one pair at a time

To run nightly:
```bash
export ITK_NIGHTLY_RUN=TRUE
./run_itk.sh
```

Combinations this SDK cannot serve are recorded in `a2a-itk/known_failures.yaml` and
skipped with the reason logged, rather than being left out of the scenario set:
gRPC (no server in the SDK), push notifications (`PushNotificationNotSupported`
everywhere), and v0.3 peers over HTTP+JSON (`A2A.V0_3Compat` is JSON-RPC only).

### v0.3 peers

`Itk.csproj` references `A2A.V0_3Compat`, so the agent both serves v0.3 clients and
dials v0.3 peers. `/jsonrpc` and `/` answer either dialect — `V03ServerProcessor`
picks one from the `A2A-Version` header and passes a 1.0 request straight through —
and the card advertises the same URL for `protocolVersion` 1.0 and 0.3.
`ItkV03.PeerEndpointAsync` reads a peer's card to decide which client to build.

## ACTS conformance

The same script runs the ACTS conformance suite instead of the traversal one, which
measures this SDK against the A2A specification rather than against its peers:

```bash
ITK_ACTS_RUN=1 ITK_ACTS_TRANSPORTS=jsonrpc,rest ./run_itk.sh
```

Two bindings, not three: the SDK has no gRPC server, so a gRPC pass has nothing to dial.

Each transport leaves a full spec §13 report as `acts-report-dotnet-<transport>-<ts>.json`,
which is what says *why* a test failed. `acts/sut-behaviors.yaml` declares which `tck-*`
behaviours the agent implements; `ActsBehaviors.cs` implements them, `ActsAuth.cs` handles
the credential-gated tests, and `ActsClientParse.cs` runs canonical payloads through this
SDK's own client for the §10 CLIENT-* tests.

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
