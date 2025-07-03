using System.Text.Json.Nodes;

namespace A2A.UnitTests.JsonRpc;

public class JsonRpcErrorResponseTests
{
    [Fact]
    public void JsonRpcErrorResponse_Properties_SetAndGet()
    {
        // Arrange
        var error = new JsonRpcError { Code = 123, Message = "err" };

        // Act
        var sut = new JsonRpcErrorResponse
        {
            Id = "id1",
            JsonRpc = "2.0",
            Error = error
        };

        // Assert
        Assert.Equal("id1", sut.Id);
        Assert.Equal("2.0", sut.JsonRpc);
        Assert.Equal(error, sut.Error);
    }

    [Fact]
    public void JsonRpcErrorResponse_CanSetResult()
    {
        // Arrange
        var node = JsonValue.Create(42);

        // Act
        var sut = new JsonRpcErrorResponse { Result = node };

        // Assert
        Assert.Equal(42, sut.Result!.GetValue<int>());
    }
}
