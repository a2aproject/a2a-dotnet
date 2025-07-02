namespace A2A.UnitTests.JsonRpc;

public class InvalidParamsErrorTests
{
    [Fact]
    public void InvalidParamsError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new InvalidParamsError();
        
        // Assert
        Assert.Equal(-32602, sut.Code);
        Assert.Equal("Invalid parameters", sut.Message);
    }
}
