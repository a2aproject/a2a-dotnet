using System.Text.Json;

namespace A2A.UnitTests.Models;

public sealed class BaseKindDiscriminatorConverterEdgeTests
{
    // Using Part as it has unknown/count semantics baked in and a short payload

    [Fact]
    public void Part_Deserialize_Kind_Index_OutOfRange_ThrowsUnknownKind()
    {
        // Arrange: craft an enum value beyond mapping length
        // PartKind.Count = 4, mapping is length 4 with index 0 null (Unknown), valid indices 1..3
        const string json = "{ \"kind\": \"count\" }";

        // Act
        var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<Part>(json, A2AJsonUtilities.DefaultOptions));

        // Assert: should hit the mapping range check and throw Unknown kind
        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
    }

    [Fact]
    public void A2AEvent_Deserialize_Kind_Index_Zero_Null_Mapping_ThrowsUnknownKind()
    {
        // Arrange: A2AEventKind.Unknown maps to index 0 which is null in the mapping
        const string json = "{ \"kind\": \"unknown\" }";

        // Act
        var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<A2AEvent>(json, A2AJsonUtilities.DefaultOptions));

        // Assert
        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
    }
}
