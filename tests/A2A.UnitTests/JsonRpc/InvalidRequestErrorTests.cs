namespace A2A.UnitTests.JsonRpc;

public class InvalidRequestErrorTests
{
    [Fact]
    public void InvalidRequestError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new InvalidRequestError();
        
        // Assert
        Assert.Equal(-32600, sut.Code);
        Assert.Equal("Request payload validation error", sut.Message);
    }
}
