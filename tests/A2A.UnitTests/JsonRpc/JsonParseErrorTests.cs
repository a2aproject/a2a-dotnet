namespace A2A.UnitTests.JsonRpc;

public class JsonParseErrorTests
{
    [Fact]
    public void JsonParseError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new JsonParseError();
        
        // Assert
        Assert.Equal(-32700, sut.Code);
        Assert.Equal("Invalid JSON payload", sut.Message);
    }
}
