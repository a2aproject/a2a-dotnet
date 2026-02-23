# Security Guide

This document describes the security model for the A2A .NET SDK, including what the SDK enforces, what is delegated to the application, and developer guidance for each trust boundary.

> **Summary for application developers:** The SDK handles protocol mechanics (serialization, JSON-RPC dispatch, task lifecycle). Authentication, authorization, HTTPS, rate limiting, webhook safety, tenant isolation, and production storage are **your responsibility** as the application author.

---

## Security Assumptions

The following assumptions define the responsibility boundary between the SDK and the application. These are not optional considerations — each one describes a class of vulnerabilities that will arise if the assumption is violated.

### 1. Authentication and Authorization (ASP.NET Core)

The A2A protocol expects authentication at the HTTP layer, not in the JSON-RPC payload. The SDK does **not** apply any authentication or authorization to A2A endpoints.

**Required application controls:**
- Apply authentication middleware (JWT bearer, API key, mTLS, etc.) to all A2A endpoints, including `/.well-known/agent-card.json` for protected agents.
- Enforce authorization per endpoint, per method, and per tenant.
- Use HTTPS/TLS in production; set HSTS headers where appropriate.
- Apply rate limiting and request body size limits.
- Ensure exception handling middleware does not return raw exception messages to clients (see [Error Responses](#error-responses)).
- Use structured logging with appropriate PII filtering; never log credentials or tokens.

> The A2AJsonRpcProcessor maps unexpected exceptions to `ProblemDetails` using `ex.Message`. In production, ensure your exception handling middleware overrides this so internal details are not returned to callers.

See: [A2A specification — Authentication and Authorization](https://a2a-protocol.org/v0.3.0/specification/#4-authentication-and-authorization)

### 2. JSON Parsing Limits

The SDK uses `System.Text.Json` for all serialization and deserialization, with source-generated metadata via `A2AJsonUtilities`. STJ's default maximum depth (64) and other default limits apply.

**Required application controls:**
- Configure request body size limits in Kestrel / reverse proxy (e.g., `MaxRequestBodySize`).
- Enforce strict `Content-Type: application/json` on JSON-RPC endpoints.
- Be aware that oversized or deeply nested payloads can cause memory pressure or CPU spikes before limits are reached.

See: [System.Text.Json Threat Model](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Json/docs/ThreatModel.md)

### 3. Streaming Resource Management

Streaming (Server-Sent Events via `message/stream` and `tasks/resubscribe`) uses long-lived HTTP connections. The SDK does not enforce any streaming resource limits.

**Required application controls:**
- Set concurrency limits per tenant, client, or IP.
- Configure idle timeouts and maximum stream durations in Kestrel.
- Implement backpressure and buffering limits to prevent slow-reader attacks.
- Apply a maximum event size and total streamed bytes per task.
- Redact sensitive intermediate content (e.g., LLM reasoning traces, partial PII) from streamed events before they are sent.

### 4. Webhook / Push Notification Security

Push notifications introduce an outbound HTTP request path whose URL and token are supplied by clients. The SDK stores `PushNotificationConfig` (including `Token` and `Authentication` fields) but **does not**:
- Validate webhook URLs for SSRF (private ranges, localhost, link-local).
- Perform URL allowlisting.
- Verify webhook request authenticity.
- Provide a constant-time token comparison API.

**Required application controls (agent side — sending notifications):**
- Validate webhook URLs before storing or using them:
  - Block localhost, private ranges (RFC 1918), and link-local ranges.
  - Require HTTPS scheme.
  - Consider a per-tenant domain allowlist.
- Apply egress firewalling to the agent process for the strongest protection.
- Use an ownership verification challenge before enabling notifications.
- Use a conservative retry policy (exponential backoff with caps) to prevent retry storms.

**Required application controls (client side — receiving notifications):**
- Authenticate incoming webhook requests using the configured token.
- Perform token comparison in a constant-time manner to prevent timing attacks.
- Validate that `taskId` belongs to the expected tenant/principal; ignore unknown task IDs.
- Consider anti-replay measures (timestamp/nonce) depending on your auth scheme.

> **Note:** The SDK stores webhook tokens but does not expose a validation helper. Implement token comparison manually in your webhook receiver.

See: [A2A specification — Push Notification Config](https://a2a-protocol.org/v0.3.0/specification/#68-pushnotificationconfig-object), [Security Considerations for Push Notifications](https://a2a-protocol.org/v0.3.0/topics/streaming-and-async/#security-considerations-for-push-notifications)

### 5. File Handling

The SDK treats `FilePart` payloads (both `bytes` and `uri` variants) as opaque data. It does not interpret, parse, decompress, scan, or validate file content.

**Required application controls:**
- Apply size limits to base64-encoded file bytes.
- Enforce MIME type allowlists appropriate for your use case.
- Perform malware scanning where file content may be executed or displayed.
- Never auto-execute received file content.
- Treat all file URIs as untrusted: validate schemes, block internal network targets, and avoid acting as an open redirect or SSRF proxy.

### 6. Tenant Isolation

The SDK does not enforce any tenant boundaries. Task IDs, push notification configs, and artifacts are not inherently scoped to a caller identity.

**Required application controls:**
- Derive tenant context from the authenticated principal (claims, API key mapping, or mTLS identity) — never from client-supplied request fields.
- Persist tenant and principal ownership metadata alongside every task and push notification config.
- Enforce tenant scoping on every `tasks/get`, `tasks/cancel`, `tasks/resubscribe`, and push config API call.
- Use non-predictable task IDs (UUIDs) to prevent enumeration.

### 7. Task Storage and Persistence

`InMemoryTaskStore` is provided for **development and testing only**. It has no persistence, no access controls, no encryption at rest, and no retention policy.

**Required application controls for production:**
- Implement `ITaskStore` backed by a persistent store (database, distributed cache, etc.).
- Enforce encryption at rest and in transit for stored task data.
- Apply per-tenant task quotas and artifact size/count limits.
- Implement TTL-based cleanup for abandoned tasks to prevent unbounded growth.
- Control access to the backing store with least-privilege credentials.

> `DistributedCacheTaskStore` is provided as an `IDistributedCache` integration point, but configuring the cache itself (TTL, eviction, persistence) is the application's responsibility.

---

## Non-Goals

The SDK does not guarantee, and cannot provide, protection for:

| Non-goal | Explanation |
|----------|-------------|
| Tenant isolation correctness | Tenant ID and isolation strategy are entirely application-defined. |
| Prompt injection / cross-agent instruction smuggling | Requires application-level control design (e.g., approval gates, tool permission isolation). Treat all remote agent outputs as untrusted. |
| Webhook egress safety (SSRF) | Requires explicit URL validation and/or egress firewalling in the application. |
| Data retention, archival, or lifecycle compliance | No retention requirement is specified by the SDK; applications must implement their own policy. |

---

## Trust Boundaries Summary

| Boundary | What the SDK does | What the application must do |
|----------|------------------|------------------------------|
| Network (Client ↔ Agent HTTP) | Routes and dispatches JSON-RPC; maps errors to ProblemDetails | Authenticate, authorize, enforce HTTPS, rate-limit, size-limit |
| Streaming (SSE) | Writes incremental events to the response stream | Concurrency limits, idle timeouts, backpressure, content redaction |
| Webhooks (Agent → Client callback) | Stores `PushNotificationConfig`; delivery is app-implemented | URL validation (SSRF), token verification, retry caps |
| Tenant isolation | None | Scope every task and config operation to authenticated identity |
| Storage | Provides `ITaskStore` interface + `InMemoryTaskStore` (dev only) | Production store, encryption, quotas, TTL cleanup |

---

## Error Responses

`A2AException` messages are intentionally returned to clients as protocol-level error details (they represent expected protocol errors such as unknown task IDs or invalid request formats).

Unexpected exceptions are currently mapped to `ProblemDetails` using `ex.Message`. **Ensure your production exception handling middleware replaces or sanitizes this** so that internal exception messages, stack traces, connection strings, or secrets are never returned to callers.

---

## References

- [A2A Protocol Specification — Authentication and Authorization](https://a2a-protocol.org/v0.3.0/specification/#4-authentication-and-authorization)
- [A2A Protocol Specification — Security Considerations](https://a2a-protocol.org/v0.3.0/specification/#102-security-considerations-summary)
- [A2A Protocol Specification — Agent Card Security](https://a2a-protocol.org/v0.3.0/specification/#54-security-of-agent-cards)
- [A2A Streaming and Push Notifications — Security Considerations](https://a2a-protocol.org/v0.3.0/topics/streaming-and-async/#security-considerations-for-push-notifications)
- [A2A Enterprise Implementation Guide](https://a2a-protocol.org/v0.3.0/topics/enterprise-ready/)
- [System.Text.Json Threat Model](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Json/docs/ThreatModel.md)
- [ASP.NET Core Security Documentation](https://docs.microsoft.com/aspnet/core/security/)
- [Kestrel — Request limits](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options)
