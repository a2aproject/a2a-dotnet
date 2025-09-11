using System.Reflection;
using Xunit.Abstractions;

namespace A2A.TCK.Tests.Infrastructure;

/// <summary>
/// Base class for all TCK tests that handles compliance level evaluation.
/// Tests will only fail if they hit "Non-compliant" status, otherwise they pass
/// with appropriate output indicating the compliance level.
/// </summary>
public abstract class TckTestBase
{
    protected readonly ITestOutputHelper Output;

    protected TckTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

    /// <summary>
    /// Evaluates a test result based on TCK compliance levels.
    /// Only fails the test if the actual behavior indicates non-compliance.
    /// </summary>
    /// <param name="testPassed">Whether the test logic passed</param>
    /// <param name="testMethod">The test method being executed</param>
    protected void EvaluateTckCompliance(bool testPassed, [System.Runtime.CompilerServices.CallerMemberName] string testMethod = "")
    {
        var method = GetType().GetMethod(testMethod);
        var tckAttribute = method?.GetCustomAttribute<TckTestAttribute>();
        
        if (tckAttribute == null)
        {
            // If no TCK attribute is found, treat as a regular test
            Assert.True(testPassed, $"Test {testMethod} failed");
            return;
        }

        var complianceLevel = tckAttribute.ComplianceLevel;
        var category = tckAttribute.Category;

        if (testPassed)
        {
            // Test passed - report the compliance level achieved
            var badge = GetComplianceBadge(complianceLevel);
            Output.WriteLine($"✅ {badge} - {category}");
            if (!string.IsNullOrEmpty(tckAttribute.Description))
            {
                Output.WriteLine($"   Description: {tckAttribute.Description}");
            }
        }
        else
        {
            // Test failed - only fail if it's marked as non-compliant
            var badge = GetComplianceBadge(complianceLevel);
            
            if (complianceLevel == TckComplianceLevel.NonCompliant)
            {
                Output.WriteLine($"❌ {badge} - {category}");
                if (!string.IsNullOrEmpty(tckAttribute.FailureImpact))
                {
                    Output.WriteLine($"   Impact: {tckAttribute.FailureImpact}");
                }
                Assert.Fail($"Non-compliant behavior detected in {category}: {tckAttribute.Description ?? testMethod}");
            }
            else
            {
                // Test failed but it's not critical - pass with informational message
                Output.WriteLine($"⚠️  {badge} - {category} (Feature not implemented)");
                if (!string.IsNullOrEmpty(tckAttribute.Description))
                {
                    Output.WriteLine($"   Description: {tckAttribute.Description}");
                }
                Output.WriteLine($"   Status: Feature not implemented - this is acceptable for {complianceLevel} level");
            }
        }
    }

    /// <summary>
    /// Asserts that a condition is true for TCK compliance.
    /// </summary>
    /// <param name="condition">The condition to check</param>
    /// <param name="message">Message to display</param>
    protected void AssertTckCompliance(bool condition, string message)
    {
        if (!condition && !string.IsNullOrEmpty(message))
        {
            Output.WriteLine($"Test condition failed: {message}");
        }
        
        EvaluateTckCompliance(condition);
    }

    private static string GetComplianceBadge(TckComplianceLevel level) => level switch
    {
        TckComplianceLevel.Mandatory => "🟢 Mandatory Compliant",
        TckComplianceLevel.Recommended => "🟡 Recommended Feature",
        TckComplianceLevel.FullFeatured => "🔵 Full Featured",
        TckComplianceLevel.NonCompliant => "🔴 Non-Compliant",
        _ => "⚫ Unknown Compliance Level"
    };

    /// <summary>
    /// Creates a test agent card for testing purposes.
    /// </summary>
    protected static AgentCard CreateTestAgentCard() => new()
    {
        Name = "Test A2A Agent",
        Description = "A test agent for A2A TCK compliance testing",
        Url = "https://example.com/agent",
        Version = "1.0.0-test",
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Skills = new List<AgentSkill>
        {
            new()
            {
                Id = "test-skill",
                Name = "Test Skill",
                Description = "A test skill for TCK testing",
                Tags = ["test", "tck"]
            }
        }
    };

    /// <summary>
    /// Helper method to create a test message for message/send tests.
    /// </summary>
    /// <param name="text">The text content for the message</param>
    /// <returns>A test message with the specified text</returns>
    protected static AgentMessage CreateTestMessage(string text = "Hello, this is a test message from the A2A TCK test suite.") => new()
    {
        Parts = new List<Part>
        {
            new TextPart { Text = text }
        },
        MessageId = Guid.NewGuid().ToString(),
        Role = MessageRole.User
    };
}