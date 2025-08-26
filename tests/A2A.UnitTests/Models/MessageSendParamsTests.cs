using System.Text.Json;

namespace A2A.UnitTests.Models
{
    public sealed class MessageSendParamsTests
    {
        [Fact]
        public void MessageSendParams_Deserialize_NonMessageKind_Throws()
        {
            // Arrange
            const string json = """
        {
            "message": {
                "kind": "task",
                "id": "t-13",
                "contextId": "c-13",
                "status": { "state": "submitted" }
            }
        }
        """;

            // Act / Assert
            var ex = Assert.Throws<A2AException>(() => JsonSerializer.Deserialize<MessageSendParams>(json, A2AJsonUtilities.DefaultOptions));
            Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        }

        [Fact]
        public void MessageSendParams_Serialized_HasKindOnMessage()
        {
            // Arrange
            var msp = new MessageSendParams
            {
                Message = new Message
                {
                    Role = MessageRole.User,
                    MessageId = "m-8",
                    Parts = [new TextPart { Text = "hello" }]
                }
            };

            var serialized = JsonSerializer.Serialize(msp, A2AJsonUtilities.DefaultOptions);

            Assert.Contains("\"kind\":\"message\"", serialized);
        }

        [Fact]
        public void MessageSendParams_SerializationRoundTrip_Succeeds()
        {
            // Arrange
            var msp = new MessageSendParams
            {
                Message = new Message
                {
                    Role = MessageRole.User,
                    MessageId = "m-8",
                    Parts = [new TextPart { Text = "hello" }]
                }
            };

            var serialized = JsonSerializer.Serialize(msp, A2AJsonUtilities.DefaultOptions);

            var deserialized = JsonSerializer.Deserialize<MessageSendParams>(serialized, A2AJsonUtilities.DefaultOptions);

            Assert.NotNull(deserialized);
            Assert.Equal(msp.Message.Role, deserialized.Message.Role);
            Assert.Equal(msp.Message.MessageId, deserialized.Message.MessageId);
            Assert.NotNull(deserialized.Message.Parts);
            Assert.Single(deserialized.Message.Parts);
            var part = Assert.IsType<TextPart>(deserialized?.Message.Parts[0]);
            Assert.Equal("hello", part.Text);
        }
    }
}
