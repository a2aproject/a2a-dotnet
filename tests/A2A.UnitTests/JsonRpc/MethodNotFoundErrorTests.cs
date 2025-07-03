namespace A2A.UnitTests.JsonRpc;

public class MethodNotFoundErrorTests
{
    [Fact]
    public void MethodNotFoundError_HasExpectedCodeAndMessage()
    {
        // Act
        var sut = new MethodNotFoundError();

        // Assert
        Assert.Equal(-32601, sut.Code);
        Assert.Equal("Method not found", sut.Message);
    }
}
