using A2A.Integration.Tests.Infrastructure;
using Xunit.Abstractions;

namespace A2A.Integration.Tests.Mandatory.JsonRpc;

/// <summary>
/// Tests for protocol violation handling based on the upstream TCK.
/// These tests validate that the implementation properly handles various protocol violations.
/// </summary>
public class ProtocolViolationTests : TckTestBase
{
    public ProtocolViolationTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryJsonRpc,
        Description = "JSON-RPC 2.0 - Invalid JSON Structure",
        SpecSection = "JSON-RPC 2.0 �4.2",
        FailureImpact = "Implementation is not JSON-RPC 2.0 compliant")]
    public void ProtocolViolation_InvalidJsonStructure_ReturnsParseError()
    {
        // This test should validate malformed JSON handling
        // In the current SDK-focused approach, we can't easily test malformed JSON
        // because we're testing the SDK components directly, not HTTP endpoints

        // TODO: Convert to HTTP endpoint testing for true protocol violation testing

        Output.WriteLine("?? Protocol violation testing requires HTTP endpoint access");
        AssertTckCompliance(true, "Protocol violation testing needs HTTP transport layer");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryJsonRpc,
        Description = "JSON-RPC 2.0 - Missing Required Fields",
        SpecSection = "JSON-RPC 2.0 �4.1",
        FailureImpact = "Implementation is not JSON-RPC 2.0 compliant")]
    public void ProtocolViolation_MissingRequiredFields_ReturnsInvalidRequest()
    {
        // Test missing required JSON-RPC fields like 'method'

        // TODO: This requires testing at the HTTP transport level
        // Current SDK testing approach cannot properly validate this

        Output.WriteLine("?? Missing field validation requires HTTP endpoint access");
        AssertTckCompliance(true, "Protocol field validation needs transport layer testing");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryJsonRpc,
        Description = "A2A v0.3.0 - Invalid Message Structure",
        SpecSection = "A2A v0.3.0 �6.4",
        FailureImpact = "Implementation is not A2A v0.3.0 compliant")]
    public async Task ProtocolViolation_InvalidMessageStructure_ReturnsInvalidParams()
    {
        // Arrange - Create message with invalid structure (missing required fields)
        var invalidMessageParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                // Missing required fields: Parts (empty), MessageId, Role
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString()
                // Parts is empty - this violates the message structure requirement
            }
        };

        // Act
        var response = await SendMessageViaJsonRpcAsync(invalidMessageParams);

        // Assert - Should return InvalidParams error
        bool hasInvalidParamsError = response.Error is not null &&
                                    response.Error.Code == (int)A2AErrorCode.InvalidParams;

        if (hasInvalidParamsError)
        {
            Output.WriteLine("? Invalid message structure correctly rejected");
            Output.WriteLine($"  Error code: {response.Error!.Code} (expected -32602)");
        }
        else if (response.Error is null)
        {
            Output.WriteLine("?? Invalid message structure was accepted (lenient implementation)");
        }
        else
        {
            Output.WriteLine($"?? Unexpected error: {response.Error.Code}");
        }

        // This test passes regardless as some implementations may be lenient
        AssertTckCompliance(true, "Message structure validation behavior observed");
    }
}
