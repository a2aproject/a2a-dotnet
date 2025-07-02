using System.Text.Json;

namespace A2A.UnitTests.JsonRpc;

public class JsonRpcErrorTests
{
    [Fact]
    public void JsonRpcError_Properties_SetAndGet()
    {
        // Arrange
        var data = JsonDocument.Parse("{\"foo\":123}").RootElement;
        
        // Act
        var sut = new JsonRpcError { Code = 42, Message = "msg", Data = data };
        
        // Assert
        Assert.Equal(42, sut.Code);
        Assert.Equal("msg", sut.Message);
        Assert.Equal(123, sut.Data?.GetProperty("foo").GetInt32());
    }

    [Fact]
    public void JsonRpcError_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var sut = new JsonRpcError { Code = 1, Message = "m", Data = JsonDocument.Parse("{\"bar\":true}").RootElement };
        
        // Act
        var json = sut.ToJson();
        var doc = JsonDocument.Parse(json);
        var deserialized = JsonRpcError.FromJson(doc.RootElement);
        
        // Assert
        Assert.Equal(sut.Code, deserialized.Code);
        Assert.Equal(sut.Message, deserialized.Message);
        Assert.Equal(sut.Data?.GetProperty("bar").GetBoolean(), deserialized.Data?.GetProperty("bar").GetBoolean());
    }
}
