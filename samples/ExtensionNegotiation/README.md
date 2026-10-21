# Extension negotiation sample

Run one agent with a small, illustrative debate extension over JSON-RPC, HTTP+JSON
and gRPC. The program makes real loopback calls, checks the results, then stops.
It needs no model credentials and does not run a real debate or generate a product
document. The extension URI and operation identifiers are example identifiers,
not an official extension specification.

## Run the sample

From the repository root, with the .NET 10 SDK:

```bash
dotnet run --project samples/ExtensionNegotiation --framework net10.0
```

The project also targets .NET 8:

```bash
dotnet run --project samples/ExtensionNegotiation --framework net8.0
```

The default listeners are IPv4 loopback ports 5080 for HTTP/1.1 and 5081 for
plaintext HTTP/2 gRPC. If either port is occupied, choose two different free ports:

```bash
dotnet run --project samples/ExtensionNegotiation --framework net10.0 -- --http-port 15080 --grpc-port 15081
```

The sample exits with an error if a check fails and has a 30-second execution
timeout. A successful run ends with:

```text
Extension negotiation demo passed.
```

The deliberately missing task may produce an `A2A.REST` error log for
`missing-demo-task`. That error is intentional; the final success line is printed
only after the expected error code and activation metadata have been checked.

These listeners and plaintext transport are for local demonstration, not production
hosting. The HTTP clients disable proxies, redirects and cookies for this local
run. Production deployments need their own TLS, authentication, authorization and
resource-isolation policies.

## Read the code

1. [DebateOperations.cs](DebateOperations.cs) registers one supported extension,
   assigns it a unary and a streaming operation, and reads the negotiated
   `A2ARequestContext` in those handlers. Registration alone does not activate it.
2. [Program.cs](Program.cs) supplies an availability policy, binds the same handles
   to the three transports, serves a registry-consistent Agent Card, and reads
   `RequestContext.ExtensionActivation` in the standard echo agent.
3. [NegotiationDemo.cs](NegotiationDemo.cs) acts as the caller and verifies the
   complete request and response behavior.

The demo verifies that an extension-owned operation cannot run without activation,
including direct registry invocation with no negotiated context. HTTP calls asking
for a different extension version also fail without running that operation.
Successful calls expose the same activation snapshot to the handler and the caller.

During each stream, `DemoAvailability.IsAvailable` changes to `false` after the first
chunk. Both chunks remain active for the current request; the next request is
declined. The chunks are produced from in-memory data. This does not promise that
a real dependency will stay usable or that an in-flight operation cannot fail.

Each SDK client checks that the standard echo agent receives activation on unary
and streaming sends, without accessing HTTP/gRPC state. It then requests a missing
standard task. Its `A2AException` retains
`TaskNotFound` while reporting the successfully activated debate extension.
Activation and execution success are separate facts.

## Understand the client boundary

The typed custom-operation handles belong to the server registry. The HTTP custom
calls here use their JSON-RPC method or HTTP route directly; the gRPC custom calls
use the SDK's generated `A2AExtensionServiceClient` envelope. This sample does not
introduce a typed custom-operation API on `IA2AClient` or claim that this gRPC
envelope is a protocol-standard service.

The normal `A2AClient`, `A2AHttpJsonClient` and `A2AGrpcClient` are used for the
standard-agent and error checks. Their caller-owned transport adds a fixed request
header for the demo. Applications choosing different extensions concurrently
should attach the choice to each request, not mutate shared default headers.
The sample's generic gRPC calls use per-call `Metadata`.

For API examples, required extensions, card consistency and signing boundaries,
see the [extension negotiation guide](../../docs/extensions.md).
