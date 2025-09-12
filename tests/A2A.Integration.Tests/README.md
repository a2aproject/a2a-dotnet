# A2A Technology Compliance Kit (TCK) Tests

This directory contains the Technology Compliance Kit (TCK) tests for the A2A (Agent-to-Agent) protocol implementation. The TCK validates that A2A implementations conform to the [A2A Protocol Specification](https://a2a-protocol.org/) and ensures interoperability between different A2A implementations.

## Overview

The A2A TCK is a comprehensive test suite that validates:

- **Protocol Compliance**: JSON-RPC 2.0 adherence, message structure validation
- **Core Features**: Message sending, task management, streaming capabilities
- **Transport Equivalence**: Consistency across HTTP and SSE streaming transports
- **Error Handling**: Proper error codes and exception handling
- **Quality Aspects**: Performance, concurrency, resource management
- **Optional Features**: Push notifications, advanced capabilities

## Test Categories

### Mandatory Tests
These tests validate core A2A functionality that all implementations **MUST** support:

#### JSON-RPC Compliance (`JsonRpcComplianceTests`)
- JSON-RPC 2.0 request/response structure validation
- Standard error code compliance (-32700 to -32603)
- A2A-specific error codes (-32001 to -32006)
- Method validation and parameter handling

#### Protocol Tests (`AgentCardMandatoryTests`, `MessageSendMethodTests`, `TaskManagementTests`)
- Agent Card structure and validation
- Message sending via HTTP (`message/send`)
- Task creation, retrieval, and cancellation
- Task state management and lifecycle

### Optional Tests
These tests validate recommended and full-featured capabilities:

#### Capabilities (`OptionalCapabilitiesTests`)
- Streaming support (`message/stream`, `tasks/resubscribe`)
- Push notification configuration
- Advanced authentication schemes

#### Quality (`QualityTests`)
- Performance and latency requirements
- Concurrency and load handling
- Memory management and resource cleanup
- Error recovery and graceful degradation

#### Transport Equivalence (`TransportEquivalenceTests`)
- HTTP vs SSE streaming consistency
- Cross-transport task continuation
- Message structure preservation
- Error handling consistency

#### Features (`OptionalFeaturesTests`)
- Advanced message handling
- Complex task workflows
- Extended metadata support

## Compliance Levels

The TCK defines three compliance levels:

### Mandatory ??
Core functionality that all A2A implementations **MUST** support:
- Basic message sending and receiving
- Task management (create, get, cancel)
- JSON-RPC 2.0 compliance
- Standard error handling

### Recommended ??
Features that implementations **SHOULD** support for better interoperability:
- Streaming via Server-Sent Events
- Performance characteristics
- Concurrency handling
- Advanced error recovery

### Full-Featured ??
Advanced capabilities for comprehensive A2A support:
- Push notifications
- Transport equivalence
- Complex message types
- Advanced task workflows

## Running the Tests

### Prerequisites
- .NET 8.0 or .NET 9.0 SDK
- Visual Studio 2022 or Visual Studio Code with C# extension

### Run All TCK Tests
```bash
dotnet test tests/A2A.TCK.Tests/ --verbosity normal
```

### Run by Compliance Level
```bash
# Mandatory tests only
dotnet test tests/A2A.TCK.Tests/ --filter "TestCategory=MandatoryJsonRpc|TestCategory=MandatoryProtocol"

# Recommended tests
dotnet test tests/A2A.TCK.Tests/ --filter "TestCategory=OptionalQuality"

# Full-featured tests
dotnet test tests/A2A.TCK.Tests/ --filter "TestCategory=TransportEquivalence|TestCategory=OptionalCapabilities"
```

### Run by Test Category
```bash
# JSON-RPC compliance tests
dotnet test tests/A2A.TCK.Tests/ --filter "FullyQualifiedName~JsonRpc"

# Message and task management
dotnet test tests/A2A.TCK.Tests/ --filter "FullyQualifiedName~MessageSend|FullyQualifiedName~TaskManagement"

# Performance and quality tests
dotnet test tests/A2A.TCK.Tests/ --filter "FullyQualifiedName~Quality"
```

### Verbose Output
For detailed compliance information:
```bash
dotnet test tests/A2A.TCK.Tests/ --verbosity detailed --logger "console;verbosity=detailed"
```

## Understanding Test Results

### Passing Tests ?
- Implementation correctly supports the tested feature
- Complies with A2A specification requirements
- Ready for interoperability

### Failing Tests ?
- Implementation has compliance issues
- May indicate bugs or missing features
- Review test output for specific failure details

### Test Attributes
Each test includes metadata about compliance requirements:

```csharp
[TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryJsonRpc,
    Description = "JSON-RPC 2.0 Specification §4.2 - Response Structure",
    SpecSection = "JSON-RPC 2.0 §4.2",
    FailureImpact = "Implementation is not JSON-RPC 2.0 compliant")]
```

## Test Structure

### Test Base Class
All TCK tests inherit from `TckTestBase` which provides:
- Compliance assertion methods (`AssertTckCompliance`)
- Test message creation utilities
- Structured test output and reporting

### Test Categories
Tests are organized by category using the `TckCategories` enum:
- `MandatoryJsonRpc` - JSON-RPC 2.0 compliance
- `MandatoryProtocol` - Core A2A protocol features
- `OptionalQuality` - Performance and quality aspects
- `OptionalCapabilities` - Advanced capabilities
- `TransportEquivalence` - Cross-transport consistency

### Mock Infrastructure
The TCK uses `TaskManager` instances for testing:
- In-memory task storage for isolated testing
- Configurable message handlers via delegates
- Event-driven task lifecycle management

## Implementation Testing

To test your A2A implementation:

1. **Set up test infrastructure** pointing to your A2A server
2. **Configure authentication** if required by your implementation
3. **Run mandatory tests first** to validate core compliance
4. **Run optional tests** to validate advanced features
5. **Review detailed output** for specific compliance issues

### Example Test Configuration
```csharp
// Configure TCK to test against your A2A server
var client = new A2AClient("https://your-a2a-server.com/api");
var tckRunner = new TckTestRunner(client);
await tckRunner.RunComplianceTestsAsync();
```

## Contributing to the TCK

### Adding New Tests
1. Inherit from `TckTestBase`
2. Use appropriate `TckTest` attributes
3. Follow the three-phase test pattern (Arrange, Act, Assert)
4. Call `AssertTckCompliance` for final validation
5. Add detailed output for debugging

### Test Naming Convention
- Use descriptive method names: `FeatureArea_Scenario_ExpectedBehavior`
- Group related tests in the same class
- Use consistent test categories

### Documentation
- Include XML documentation for all test methods
- Reference specific sections of the A2A specification
- Describe the failure impact for failed tests

## Specification References

- [A2A Protocol Specification](https://a2a-protocol.org/)
- [JSON-RPC 2.0 Specification](https://www.jsonrpc.org/specification)
- [Server-Sent Events (SSE)](https://html.spec.whatwg.org/multipage/server-sent-events.html)

## Support

For questions about the TCK or A2A protocol compliance:
- Review the [A2A specification](https://a2a-protocol.org/)
- Check existing test implementations for examples
- Consult the test output for specific compliance issues

---

The A2A TCK ensures reliable interoperability between A2A implementations by validating conformance to the A2A protocol specification. Run these tests regularly during development to maintain compliance and catch regressions early.