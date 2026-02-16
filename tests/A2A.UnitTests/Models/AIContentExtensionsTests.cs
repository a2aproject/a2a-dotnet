using System.Text.Json;
using Microsoft.Extensions.AI;

namespace A2A.UnitTests.Models
{
    public sealed class AIContentExtensionsTests
    {
        [Fact]
        public void ToChatMessage_ThrowsOnNullMessage()
        {
            Assert.Throws<ArgumentNullException>("message", () => ((Message)null!).ToChatMessage());
        }

        [Fact]
        public void ToChatMessage_ConvertsAgentRoleAndParts()
        {
            var textPart = Part.FromText("hello");
            var urlPart = Part.FromUrl("https://example.com", "text/plain");
            var rawBytes = new byte[] { 1, 2, 3 };
            var rawPart = Part.FromRaw(rawBytes, "application/octet-stream", "b.bin");
            var dataPart = Part.FromData(JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["k"] = "v" }));
            var message = new Message
            {
                Role = Role.Agent,
                MessageId = "mid-1",
                Parts = new List<Part> { textPart, urlPart, rawPart, dataPart }
            };

            var chat = message.ToChatMessage();

            Assert.Equal(ChatRole.Assistant, chat.Role);
            Assert.Same(message, chat.RawRepresentation);
            Assert.Equal(message.MessageId, chat.MessageId);
            Assert.Equal(message.Parts.Count, chat.Contents.Count);

            // Validate all content mappings
            var c0 = Assert.IsType<TextContent>(chat.Contents[0]);
            Assert.Equal("hello", c0.Text);

            var c1 = Assert.IsType<UriContent>(chat.Contents[1]);
            Assert.Equal("text/plain", c1.MediaType);

            var c2 = Assert.IsType<DataContent>(chat.Contents[2]);
            Assert.Equal(new byte[] { 1, 2, 3 }, c2.Data);
            Assert.Equal("application/octet-stream", c2.MediaType);

            var c3 = Assert.IsType<DataContent>(chat.Contents[3]);
            Assert.Same(dataPart, c3.RawRepresentation);
        }

        [Fact]
        public void ToChatMessage_CopiesMetadataToAdditionalProperties()
        {
            var metadata = new Dictionary<string, JsonElement>
            {
                ["num"] = JsonSerializer.SerializeToElement(42),
                ["str"] = JsonSerializer.SerializeToElement("value")
            };
            var message = new Message
            {
                Role = Role.User,
                MessageId = "m-meta",
                Parts = new List<Part>(),
                Metadata = metadata
            };

            var chat = message.ToChatMessage();
            Assert.NotNull(chat.AdditionalProperties);
            Assert.Equal(2, chat.AdditionalProperties!.Count);
            Assert.True(chat.AdditionalProperties.TryGetValue("num", out var numObj));
            Assert.True(chat.AdditionalProperties.TryGetValue("str", out var strObj));
            var numJe = Assert.IsType<JsonElement>(numObj);
            var strJe = Assert.IsType<JsonElement>(strObj);
            Assert.Equal(42, numJe.GetInt32());
            Assert.Equal("value", strJe.GetString());
        }

        [Fact]
        public void ToA2AMessage_ThrowsOnNullChatMessage()
        {
            Assert.Throws<ArgumentNullException>("chatMessage", () => ((ChatMessage)null!).ToA2AMessage());
        }

        [Fact]
        public void ToA2AMessage_ReturnsExistingMessageWhenRawRepresentationMatches()
        {
            var original = new Message { MessageId = "m1", Parts = new List<Part> { Part.FromText("hi") } };
            var chat = original.ToChatMessage();

            Assert.Same(original, chat.RawRepresentation);
            Assert.Same(original, chat.ToA2AMessage());
        }

        [Fact]
        public void ToA2AMessage_GeneratesMessageIdAndConvertsParts()
        {
            var chat = new ChatMessage
            {
                Role = ChatRole.Assistant,
                MessageId = null,
                Contents = new List<AIContent>
                {
                    new TextContent("hello"),
                    new UriContent(new Uri("https://example.com/file.txt"), "text/plain")
                }
            };

            var msg = chat.ToA2AMessage();

            Assert.Equal(Role.Agent, msg.Role);
            Assert.False(string.IsNullOrWhiteSpace(msg.MessageId));
            Assert.Equal(2, msg.Parts.Count);
            Assert.NotNull(msg.Parts[0].Text);
            Assert.NotNull(msg.Parts[1].Url);
        }

        [Fact]
        public void ToAIContent_ThrowsOnNullPart()
        {
            Assert.Throws<ArgumentNullException>("part", () => ((Part)null!).ToAIContent());
        }

        [Fact]
        public void WhenTextPart_ToAIContent_ReturnsTextContent()
        {
            var part = Part.FromText("abc");
            var content = part.ToAIContent();
            var tc = Assert.IsType<TextContent>(content);
            Assert.Equal("abc", tc.Text);
            Assert.Same(part, content.RawRepresentation);
        }

        [Fact]
        public void WhenUrlPart_ToAIContent_ReturnsUriContent()
        {
            var part = Part.FromUrl("https://example.com/data.json", "application/json");
            var content = part.ToAIContent();
            var uc = Assert.IsType<UriContent>(content);
            Assert.Equal("application/json", uc.MediaType);
        }

        [Fact]
        public void WhenRawPart_ToAIContent_ReturnsDataContent()
        {
            var raw = new byte[] { 10, 20, 30 };
            var part = Part.FromRaw(raw, "image/png", "r.bin");
            var content = part.ToAIContent();
            var dc = Assert.IsType<DataContent>(content);
            Assert.Equal(raw, dc.Data);
            Assert.Equal("image/png", dc.MediaType);
        }

        [Fact]
        public void WhenDataPart_ToAIContent_ReturnsDataContent()
        {
            var part = Part.FromData(JsonSerializer.SerializeToElement(new { x = "y" }));
            part.Metadata = new Dictionary<string, JsonElement> { ["m"] = JsonSerializer.SerializeToElement(123) };
            var content = part.ToAIContent();
            Assert.IsType<DataContent>(content);
            Assert.NotNull(content.AdditionalProperties);
            Assert.True(content.AdditionalProperties!.TryGetValue("m", out var obj));
            Assert.True(obj is JsonElement je && je.GetInt32() == 123);
        }

        [Fact]
        public void ToPart_ThrowsOnNullContent()
        {
            Assert.Throws<ArgumentNullException>("content", () => ((AIContent)null!).ToPart());
        }

        [Fact]
        public void ToPart_ReturnsExistingPartWhenRawRepresentationPresent()
        {
            var part = Part.FromText("hi");
            Assert.Same(part, part.ToAIContent().ToPart());
        }

        [Fact]
        public void ToPart_ConvertsTextContent()
        {
            var content = new TextContent("hello");
            var part = content.ToPart();
            Assert.NotNull(part);
            Assert.Equal("hello", part!.Text);
        }

        [Fact]
        public void ToPart_ConvertsUriContent()
        {
            var uri = new Uri("https://example.com/a.txt");
            var content = new UriContent(uri, "text/plain");
            var part = content.ToPart();
            Assert.NotNull(part);
            Assert.NotNull(part!.Url);
            Assert.Equal("text/plain", part.MediaType);
        }

        [Fact]
        public void ToPart_ConvertsDataContent()
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            var content = new DataContent(payload, "application/custom");
            var part = content.ToPart();
            Assert.NotNull(part);
            Assert.NotNull(part!.Raw);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, part.Raw);
            Assert.Equal("application/custom", part.MediaType);
        }

        [Fact]
        public void ToPart_TransfersAdditionalPropertiesToMetadata()
        {
            var content = new TextContent("hello");
            content.AdditionalProperties ??= new();
            content.AdditionalProperties["s"] = "str";
            content.AdditionalProperties["i"] = 42;

            var part = content.ToPart();
            Assert.NotNull(part);
            Assert.NotNull(part!.Metadata);
            Assert.True(part.Metadata!.ContainsKey("s"));
            Assert.True(part.Metadata!.ContainsKey("i"));
            Assert.Equal("str", part.Metadata["s"].GetString());
            Assert.Equal(42, part.Metadata["i"].GetInt32());
        }

        [Fact]
        public void WhenMessage_ToChatMessage_ReturnsChatMessage()
        {
            var message = new Message
            {
                Role = Role.User,
                Parts = [Part.FromText("Hello")],
                MessageId = "test-id",
            };

            var chatMessage = message.ToChatMessage();

            Assert.Equal(ChatRole.User, chatMessage.Role);
            Assert.Equal("test-id", chatMessage.MessageId);
            Assert.Single(chatMessage.Contents);
        }

        [Fact]
        public void WhenChatMessage_ToA2AMessage_ReturnsMessage()
        {
            var chatMessage = new ChatMessage(ChatRole.Assistant, "Hello");

            var message = chatMessage.ToA2AMessage();

            Assert.Equal(Role.Agent, message.Role);
            Assert.NotEmpty(message.Parts);
        }
    }
}
