using System.Text.Json;

namespace A2A.V0_3.UnitTests.Models
{
    public sealed class MessageSendParamsTests
    {
        [Theory]
        [InlineData("task")]
        [InlineData("foo")]
        public void MessageSendParams_Deserialize_InvalidKind_Throws(string invalidKind)
        {
            // Arrange
            var json = $$"""
            {
                "message": {
                    "kind": "{{invalidKind}}",
                    "id": "t-13",
                    "contextId": "c-13",
                    "status": { "state": "submitted" }
                }
            }
            """;

            // Act / Assert
            var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MessageSendParams>(json, A2AJsonUtilities.DefaultOptions));
        }

        [Fact]
        public void MessageSendParams_Serialized_HasKindOnMessage()
        {
            // Arrange
            var msp = new MessageSendParams
            {
                Message = new AgentMessage
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
                Message = new AgentMessage
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

        [Fact]
        public void MessageSendConfiguration_Serialized_UsesPushNotificationConfigPropertyName()
        {
            // Arrange
            var msp = new MessageSendParams
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.User,
                    MessageId = "m-8",
                    Parts = [new TextPart { Text = "hello" }]
                },
                Configuration = new MessageSendConfiguration
                {
                    AcceptedOutputModes = ["text"],
                    PushNotification = new PushNotificationConfig
                    {
                        Url = "https://example.com/webhook"
                    }
                }
            };

            // Act
            var serialized = JsonSerializer.Serialize(msp, A2AJsonUtilities.DefaultOptions);

            // Assert
            Assert.Contains("\"pushNotificationConfig\"", serialized);
            Assert.DoesNotContain("\"pushNotification\":", serialized);
        }
    }
}
