using System.Text.Json;

namespace A2A.UnitTests.Client
{
    public class JsonRpcContentTests
    {
        [Fact]
        public async Task Constructor_SetsContentType_AndSerializesRequest()
        {
            // Arrange
            using var data = JsonDocument.Parse("{\"foo\":\"bar\"}");

            var request = new JsonRpcRequest
            {
                Id = "req-1",
                Method = "testMethod",
                Params = data.RootElement
            };

            var ms = new MemoryStream();

            var sut = new JsonRpcContent(request);

            // Act
            await sut.CopyToAsync(ms);

            ms.Position = 0;
            using var doc = await JsonDocument.ParseAsync(ms);

            // Assert
            Assert.Equal("application/json", sut.Headers.ContentType!.MediaType);
            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal("req-1", doc.RootElement.GetProperty("id").GetString());
            Assert.Equal("testMethod", doc.RootElement.GetProperty("method").GetString());
            Assert.Equal("bar", doc.RootElement.GetProperty("params").GetProperty("foo").GetString());
        }

        [Fact]
        public async Task Constructor_SetsContentType_AndSerializesResponse()
        {
            // Arrange
            var response = JsonRpcResponse.CreateJsonRpcResponse("resp-1", new AgentTask
            {
                Id = "task-1",
                ContextId = "ctx-1",
                Status = new TaskStatus { State = TaskState.Completed }
            });
            var sut = new JsonRpcContent(response);

            // Act
            var ms = new MemoryStream();
            await sut.CopyToAsync(ms);
            ms.Position = 0;
            using var doc = await JsonDocument.ParseAsync(ms);

            // Assert
            Assert.Equal("application/json", sut.Headers.ContentType!.MediaType);
            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal("resp-1", doc.RootElement.GetProperty("id").GetString());
            var result = doc.RootElement.GetProperty("result");
            Assert.Equal("task-1", result.GetProperty("id").GetString());
            Assert.Equal("ctx-1", result.GetProperty("contextId").GetString());
            Assert.Equal("TASK_STATE_COMPLETED", result.GetProperty("status").GetProperty("state").GetString());
        }

        [Fact]
        public void ContentLength_IsNull()
        {
            // Arrange
            var request = new JsonRpcRequest { Id = "id", Method = "m" };
            var sut = new JsonRpcContent(request);

            // Act
            var length = sut.Headers.ContentLength;

            // Assert
            Assert.Null(length);
        }

        [Fact]
        public async Task SerializeToStreamAsync_WritesToStream()
        {
            // Arrange
            var request = new JsonRpcRequest { Id = "id", Method = "m" };
            var sut = new JsonRpcContent(request);
            using var ms = new MemoryStream();

            // Act
            await sut.CopyToAsync(ms);

            // Assert
            Assert.True(ms.Length > 0);
        }
    }
}
