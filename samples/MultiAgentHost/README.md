# Multi-Agent Hosting Sample

Demonstrates hosting multiple A2A agents on a single ASP.NET Core server with subdomain-based routing. Each agent gets its own `A2AServer` instance with isolated task storage.

This sample addresses the scenario described in [#353](https://github.com/a2aproject/a2a-dotnet/issues/353): an enterprise platform where each customer gets a subdomain and deploys their own agents.

## Architecture

```text
                ┌─────────────────────────────────────────────────┐
                │            ASP.NET Core Server                  │
                │                                                 │
  Request ──►   │  SubdomainMiddleware                            │
                │    ├─ Extracts subdomain from Host header       │
                │    └─ Or from X-Agent-Subdomain header (dev)    │
                │                                                 │
                │  MultiAgentHandler (IA2ARequestHandler)         │
                │    ├─ Reads SubdomainContext                    │
                │    └─ Delegates to A2AServerFactory             │
                │                                                 │
                │  A2AServerFactory                               │
                │    ├─ scheduler ──► A2AServer + InMemoryStore   │
                │    ├─ research  ──► A2AServer + InMemoryStore   │
                │    └─ (more...)                                 │
                └─────────────────────────────────────────────────┘
```

Key design decisions:

- **Task isolation by construction**: each agent gets its own `InMemoryTaskStore`, so no task data leaks between agents.
- **Dynamic agent cards**: `/.well-known/agent-card.json` returns a different `AgentCard` per subdomain.
- **Lazy server creation**: `A2AServer` instances are created on first request and cached.
- **Single `MapA2A` call**: one `IA2ARequestHandler` adapter routes all requests.

## Running the Sample

### 1. Start the server

```bash
cd samples/MultiAgentHost
dotnet run --urls http://localhost:5060
```

### 2. Run the client (in another terminal)

```bash
cd samples/MultiAgentClient
dotnet run
```

The client validates:
1. Agent card discovery per subdomain
2. Message routing to the correct agent
3. Task isolation between agents
4. Cross-agent task access is blocked
5. Unknown agent subdomains return errors

### Testing Without DNS

The sample supports an `X-Agent-Subdomain` header for local testing without configuring DNS or `/etc/hosts`:

```bash
# Get the scheduler agent's card
curl -H "X-Agent-Subdomain: scheduler" http://localhost:5060/.well-known/agent-card.json

# Get the research agent's card
curl -H "X-Agent-Subdomain: research" http://localhost:5060/.well-known/agent-card.json
```

### Testing With Real Subdomains

Add to your hosts file (`C:\Windows\System32\drivers\etc\hosts` or `/etc/hosts`):

```text
127.0.0.1 scheduler.platform.local
127.0.0.1 research.platform.local
```

Then:

```bash
curl http://scheduler.platform.local:5060/.well-known/agent-card.json
curl http://research.platform.local:5060/.well-known/agent-card.json
```

## Project Structure

| File | Purpose |
|------|---------|
| `Program.cs` | Wires up middleware, registers agents, maps endpoints |
| `A2AServerFactory.cs` | Creates/caches `A2AServer` instances per subdomain |
| `MultiAgentHandler.cs` | `IA2ARequestHandler` adapter that delegates per subdomain |
| `SubdomainMiddleware.cs` | Extracts subdomain from Host header into scoped context |
| `SubdomainContext.cs` | Scoped service holding the resolved subdomain |
| `NamedEchoAgent.cs` | Simple echo `IAgentHandler` that prefixes with agent name |

## Adapting for Production

- Replace `AgentRegistration` data with a database or configuration service.
- Replace `InMemoryTaskStore` with a persistent `ITaskStore` implementation.
- Add authentication/authorization middleware.
- Configure a reverse proxy (nginx, YARP) for TLS termination if needed; Kestrel handles subdomain routing natively.
- Consider handler eviction from `A2AServerFactory` for very large numbers of tenants.
