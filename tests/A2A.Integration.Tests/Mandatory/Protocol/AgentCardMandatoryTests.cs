using Xunit.Abstractions;
using A2A.Integration.Tests.Infrastructure;

namespace A2A.Integration.Tests.Mandatory.Protocol;

/// <summary>
/// Tests for A2A Agent Card mandatory requirements based on the TCK.
/// These tests validate the structure and content requirements for Agent Cards
/// according to the A2A v0.3.0 specification.
/// </summary>
public class AgentCardMandatoryTests : TckTestBase
{
    public AgentCardMandatoryTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A Specification �5.5.1 - Basic Agent Information Required",
        SpecSection = "�5.5.1",
        FailureImpact = "Critical - violates A2A specification compliance")]
    public void AgentCard_BasicInfo_IsPresent()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();

        // Act & Assert
        var hasName = !string.IsNullOrWhiteSpace(agentCard.Name);
        var hasDescription = !string.IsNullOrWhiteSpace(agentCard.Description);
        var hasUrl = !string.IsNullOrWhiteSpace(agentCard.Url) && 
                    (agentCard.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                     agentCard.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        var hasVersion = !string.IsNullOrWhiteSpace(agentCard.Version);

        var allBasicInfoPresent = hasName && hasDescription && hasUrl && hasVersion;

        if (!hasName) Output.WriteLine("Missing required field: name");
        if (!hasDescription) Output.WriteLine("Missing required field: description");
        if (!hasUrl) Output.WriteLine("Missing required field: url (must be valid HTTP/HTTPS URL)");
        if (!hasVersion) Output.WriteLine("Missing required field: version");

        AssertTckCompliance(allBasicInfoPresent, "Agent Card must include all basic information fields");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A Specification �5.5.3/5.5.4 - Input/Output Modes Required",
        SpecSection = "�5.5.3/5.5.4",
        FailureImpact = "Critical - violates A2A specification compliance")]
    public void AgentCard_InputOutputModes_ArePresent()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();

        // Act & Assert
        var hasDefaultInputModes = agentCard.DefaultInputModes?.Count > 0;
        var hasDefaultOutputModes = agentCard.DefaultOutputModes?.Count > 0;

        var hasValidInputModes = hasDefaultInputModes && 
                                agentCard.DefaultInputModes!.All(mode => !string.IsNullOrWhiteSpace(mode));
        var hasValidOutputModes = hasDefaultOutputModes && 
                                 agentCard.DefaultOutputModes!.All(mode => !string.IsNullOrWhiteSpace(mode));

        var allModesValid = hasValidInputModes && hasValidOutputModes;

        if (!hasDefaultInputModes) Output.WriteLine("Missing or empty defaultInputModes field");
        if (!hasDefaultOutputModes) Output.WriteLine("Missing or empty defaultOutputModes field");
        if (hasDefaultInputModes && !hasValidInputModes) Output.WriteLine("defaultInputModes contains empty values");
        if (hasDefaultOutputModes && !hasValidOutputModes) Output.WriteLine("defaultOutputModes contains empty values");

        AssertTckCompliance(allModesValid, "Agent Card must include valid input and output modes");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A Specification �5.5.4 - Skills Required and Valid",
        SpecSection = "�5.5.4",
        FailureImpact = "Critical - violates A2A specification compliance")]
    public void AgentCard_Skills_AreValidAndPresent()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();

        // Act & Assert
        var hasSkills = agentCard.Skills?.Count > 0;
        
        bool allSkillsValid = true;
        if (hasSkills)
        {
            var requiredSkillFields = new[] { "id", "name", "description", "tags" };
            
            for (int i = 0; i < agentCard.Skills!.Count; i++)
            {
                var skill = agentCard.Skills[i];
                
                var hasId = !string.IsNullOrWhiteSpace(skill.Id);
                var hasName = !string.IsNullOrWhiteSpace(skill.Name);
                var hasDescription = !string.IsNullOrWhiteSpace(skill.Description);
                var hasTags = skill.Tags?.Count > 0;

                if (!hasId)
                {
                    Output.WriteLine($"Skill at index {i} missing required field 'id'");
                    allSkillsValid = false;
                }
                if (!hasName)
                {
                    Output.WriteLine($"Skill at index {i} missing required field 'name'");
                    allSkillsValid = false;
                }
                if (!hasDescription)
                {
                    Output.WriteLine($"Skill at index {i} missing required field 'description'");
                    allSkillsValid = false;
                }
                if (!hasTags)
                {
                    Output.WriteLine($"Skill at index {i} missing required field 'tags' or tags is empty");
                    allSkillsValid = false;
                }
            }
        }
        else
        {
            Output.WriteLine("Agent Card must include at least one skill");
            allSkillsValid = false;
        }

        AssertTckCompliance(allSkillsValid, "Agent Card must include valid skills with all required fields");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Mandatory, TckCategories.MandatoryProtocol,
        Description = "A2A Specification - Protocol Version Must Be Present",
        FailureImpact = "Critical - violates A2A specification compliance")]
    public void AgentCard_ProtocolVersion_IsPresent()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();

        // Act & Assert
        var hasProtocolVersion = !string.IsNullOrWhiteSpace(agentCard.ProtocolVersion);

        if (!hasProtocolVersion)
        {
            Output.WriteLine("Missing required field: protocolVersion");
        }
        else
        {
            Output.WriteLine($"Protocol version declared: {agentCard.ProtocolVersion}");
        }

        AssertTckCompliance(hasProtocolVersion, "Agent Card must include protocolVersion field");
    }

    [Fact]
    [TckTest(TckComplianceLevel.Recommended, TckCategories.MandatoryProtocol,
        Description = "A2A Specification - Security Schemes Should Be Present for Production",
        FailureImpact = "Recommended for production deployments")]
    public void AgentCard_SecuritySchemes_ShouldBePresent()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();

        // Act & Assert
        var hasSecuritySchemes = agentCard.SecuritySchemes?.Count > 0;
        var hasSecurity = agentCard.Security?.Count > 0;

        if (!hasSecuritySchemes)
        {
            Output.WriteLine("No security schemes declared - not recommended for production");
        }
        if (!hasSecurity)
        {
            Output.WriteLine("No security requirements declared - not recommended for production");
        }

        // This is a recommendation, so we pass even if not present
        AssertTckCompliance(true, "Security schemes are recommended but not mandatory for basic compliance");
        
        if (hasSecuritySchemes || hasSecurity)
        {
            Output.WriteLine("? Security configuration is present - good for production readiness");
        }
    }

    [Fact]
    [TckTest(TckComplianceLevel.FullFeatured, TckCategories.MandatoryProtocol,
        Description = "A2A Specification - Extended Agent Card Support",
        FailureImpact = "Enhanced feature for authenticated scenarios")]
    public void AgentCard_AuthenticatedExtendedCard_SupportDeclaration()
    {
        // Arrange
        var agentCard = CreateTestAgentCard();

        // Act & Assert - This is a feature test, not a compliance requirement
        var supportsExtendedCard = agentCard.SupportsAuthenticatedExtendedCard;

        Output.WriteLine($"Supports authenticated extended card: {supportsExtendedCard}");

        // This is a full-featured enhancement, so we pass regardless
        AssertTckCompliance(true, "Authenticated extended card support is an optional enhancement");
    }
}
