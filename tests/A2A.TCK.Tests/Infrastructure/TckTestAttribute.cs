namespace A2A.TCK.Tests.Infrastructure;

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
/// Categories for TCK tests matching the TCK project structure.
/// </summary>
public static class TckCategories
{
    public const string Mandatory = "Mandatory";
    public const string Capabilities = "Capabilities";
    public const string TransportEquivalence = "TransportEquivalence";
    public const string Quality = "Quality";
    public const string Features = "Features";

    // Sub-categories
    public const string MandatoryProtocol = "Mandatory.Protocol";
    public const string MandatoryJsonRpc = "Mandatory.JsonRpc";
    public const string MandatoryAuthentication = "Mandatory.Authentication";
    public const string MandatorySecurity = "Mandatory.Security";
    public const string MandatoryTransport = "Mandatory.Transport";
    public const string MandatoryQuality = "Mandatory.Quality";
    
    public const string OptionalCapabilities = "Optional.Capabilities";
    public const string OptionalFeatures = "Optional.Features";
    public const string OptionalQuality = "Optional.Quality";
    public const string OptionalMultiTransport = "Optional.MultiTransport";
}