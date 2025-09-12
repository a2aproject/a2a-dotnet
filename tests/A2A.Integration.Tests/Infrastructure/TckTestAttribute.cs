namespace A2A.Integration.Tests.Infrastructure;

/// <summary>
/// Defines compliance levels for A2A TCK tests based on the TCK specification.
/// Only tests marked as "Non-compliant" should result in test failure.
/// </summary>
public enum TckComplianceLevel
{
    /// <summary>
    /// Mandatory compliance - failure blocks SDK compliance (Critical)
    /// </summary>
    Mandatory,

    /// <summary>
    /// Recommended features - SHOULD requirements
    /// </summary>
    Recommended,

    /// <summary>
    /// Full featured - MAY requirements, nice to have
    /// </summary>
    FullFeatured,

    /// <summary>
    /// Non-compliant - indicates a serious violation that should fail the test
    /// </summary>
    NonCompliant
}

/// <summary>
/// Attribute to mark tests with their TCK compliance level and category.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class TckTestAttribute : Attribute
{
    public TckComplianceLevel ComplianceLevel { get; }
    public string Category { get; }
    public string? Description { get; set; }
    public string? SpecSection { get; set; }
    public string? FailureImpact { get; set; }

    public TckTestAttribute(TckComplianceLevel complianceLevel, string category)
    {
        ComplianceLevel = complianceLevel;
        Category = category;
    }
}

/// <summary>
/// Categories for TCK test classification
/// </summary>
public static class TckCategories
{
    // Mandatory categories - MUST requirements
    public const string MandatoryProtocol = "MandatoryProtocol";
    public const string MandatoryJsonRpc = "MandatoryJsonRpc";
    public const string MandatoryAuthentication = "MandatoryAuthentication";
    public const string MandatorySecurity = "MandatorySecurity";
    public const string MandatoryTransport = "MandatoryTransport";
    public const string MandatoryQuality = "MandatoryQuality";

    // Optional categories - SHOULD/MAY requirements
    public const string OptionalCapabilities = "OptionalCapabilities";
    public const string OptionalFeatures = "OptionalFeatures";
    public const string OptionalQuality = "OptionalQuality";
    public const string OptionalMultiTransport = "OptionalMultiTransport";

    // Unit test categories
    public const string UnitAdapters = "UnitAdapters";
    public const string UnitTransport = "UnitTransport";
}
