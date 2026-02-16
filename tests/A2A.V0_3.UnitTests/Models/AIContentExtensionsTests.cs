using System.Text.Json;
using Microsoft.Extensions.AI;

namespace A2A.V0_3.UnitTests.Models
{
    public sealed class AIContentExtensionsTests
    {
        [Fact]
        public void ToChatMessage_ThrowsOnNullAgentMessage()
        {
            Assert.Throws<ArgumentNullException>("agentMessage", () => ((AgentMessage)null!).ToChatMessage());
        }

        [Fact]
        public void ToChatMessage_ConvertsAgentRoleAndParts()
        {
            var text = new TextPart { Text = "hello" };
            var file = new FilePart { File = new FileContent(new Uri("https://example.com")) { MimeType = "text/plain" } };
            var bytes = new byte[] { 1, 2, 3 };
            var fileBytes = new FilePart { File = new FileContent(Convert.ToBase64String(bytes)) { MimeType = "application/octet-stream", Name = "b.bin" } };
            var data = new DataPart { Data = new Dictionary<string, JsonElement> { ["k"] = JsonSerializer.SerializeToElement("v") } };
            var agent = new AgentMessage
            {
                Role = MessageRole.Agent,
                MessageId = "mid-1",
                Parts = new List<Part> { text, file, fileBytes, data }
            };

            var chat = agent.ToChatMessage();

            Assert.Equal(ChatRole.Assistant, chat.Role);
            Assert.Same(agent, chat.RawRepresentation);
            Assert.Equal(agent.MessageId, chat.MessageId);
            Assert.Equal(agent.Parts.Count, chat.Contents.Count);

            // Validate all content mappings
            var c0 = Assert.IsType<TextContent>(chat.Contents[0]);
            Assert.Equal("hello", c0.Text);

            var c1 = Assert.IsType<UriContent>(chat.Contents[1]);
            Assert.Equal(new Uri("https://example.com"), c1.Uri);
            Assert.Equal("text/plain", c1.MediaType);

            var c2 = Assert.IsType<DataContent>(chat.Contents[2]);
            Assert.Equal(new byte[] { 1, 2, 3 }, c2.Data);
            Assert.Equal("application/octet-stream", c2.MediaType);

            var c3 = Assert.IsType<DataContent>(chat.Contents[3]);
            Assert.Same(data, c3.RawRepresentation);
        }

        [Fact]
        public void ToChatMessage_CopiesMetadataToAdditionalProperties()
        {
            var metadata = new Dictionary<string, JsonElement>
            {
                ["num"] = JsonSerializer.SerializeToElement(42),
                ["str"] = JsonSerializer.SerializeToElement("value")
            };
            var agent = new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = "m-meta",
                Parts = new List<Part>(),
                Metadata = metadata
            };

            var chat = agent.ToChatMessage();
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
        public void ToAgentMessage_ThrowsOnNullChatMessage()
        {
            Assert.Throws<ArgumentNullException>("chatMessage", () => ((ChatMessage)null!).ToAgentMessage());
        }

        [Fact]
        public void ToAgentMessage_ReturnsExistingAgentMessageWhenRawRepresentationMatches()
        {
            var original = new AgentMessage { MessageId = "m1", Parts = new List<Part> { new TextPart { Text = "hi" } } };
            var chat = original.ToChatMessage();

            Assert.Same(original, chat.RawRepresentation);
            Assert.Same(original, chat.ToAgentMessage());
        }

        [Fact]
        public void ToAgentMessage_GeneratesMessageIdAndConvertsParts()
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

            var msg = chat.ToAgentMessage();

            Assert.Equal(MessageRole.Agent, msg.Role);
            Assert.False(string.IsNullOrWhiteSpace(msg.MessageId));
            Assert.Equal(2, msg.Parts.Count);
            Assert.IsType<TextPart>(msg.Parts[0]);
            Assert.IsType<FilePart>(msg.Parts[1]);
        }

        [Fact]
        public void ToAIContent_ThrowsOnNullPart()
        {
            Assert.Throws<ArgumentNullException>("part", () => ((Part)null!).ToAIContent());
        }

        [Fact]
        public void ToAIContent_ConvertsTextPart()
        {
            var part = new TextPart { Text = "abc" };
            var content = part.ToAIContent();
            var tc = Assert.IsType<TextContent>(content);
            Assert.Equal("abc", tc.Text);
            Assert.Same(part, content.RawRepresentation);
        }

        [Fact]
        public void ToAIContent_ConvertsFilePartWithUri()
        {
            var uri = new Uri("https://example.com/data.json");
            var part = new FilePart { File = new FileContent(uri) { MimeType = "application/json" } };
            var content = part.ToAIContent();
            var uc = Assert.IsType<UriContent>(content);
            Assert.Equal(uri, uc.Uri);
            Assert.Equal("application/json", uc.MediaType);
        }

        [Fact]
        public void ToAIContent_ConvertsFilePartWithBytes()
        {
            var raw = new byte[] { 10, 20, 30 };
            var b64 = Convert.ToBase64String(raw);
            var part = new FilePart { File = new FileContent(b64) { MimeType = null, Name = "r.bin" } };
            var content = part.ToAIContent();
            var dc = Assert.IsType<DataContent>(content);
            Assert.Equal(raw, dc.Data);
            Assert.Equal("application/octet-stream", dc.MediaType); // default applied
        }

        [Fact]
        public void ToAIContent_ConvertsDataPartWithMetadata()
        {
            var metaValue = JsonSerializer.SerializeToElement(123);
            var part = new DataPart
            {
                Data = new Dictionary<string, JsonElement> { ["x"] = JsonSerializer.SerializeToElement("y") },
                Metadata = new Dictionary<string, JsonElement> { ["m"] = metaValue }
            };
            var content = part.ToAIContent();
            Assert.IsType<DataContent>(content);
            Assert.NotNull(content.AdditionalProperties);
            Assert.True(content.AdditionalProperties!.TryGetValue("m", out var obj));
            Assert.True(obj is JsonElement je && je.GetInt32() == 123);
        }

        [Fact]
        public void ToAIContent_FallsBackToBaseAIContentForUnknownPart()
        {
            var part = new CustomPart();
            var content = part.ToAIContent();
            Assert.Equal(typeof(AIContent), content.GetType());
            Assert.Same(part, content.RawRepresentation);
        }

        [Fact]
        public void ToPart_ThrowsOnNullContent()
        {
            Assert.Throws<ArgumentNullException>("content", () => ((AIContent)null!).ToPart());
        }

        [Fact]
        public void ToPart_ReturnsExistingPartWhenRawRepresentationPresent()
        {
            var part = new TextPart { Text = "hi" };
            Assert.Same(part, part.ToAIContent().ToPart());
        }

        [Fact]
        public void ToPart_ConvertsTextContent()
        {
            var content = new TextContent("hello");
            var part = content.ToPart();
            var tp = Assert.IsType<TextPart>(part);
            Assert.Equal("hello", tp.Text);
        }

        [Fact]
        public void ToPart_ConvertsUriContent()
        {
            var uri = new Uri("https://example.com/a.txt");
            var content = new UriContent(uri, "text/plain");
            var part = content.ToPart();
            var fp = Assert.IsType<FilePart>(part);
            Assert.NotNull(fp.File.Uri);
            Assert.Equal(uri, fp.File.Uri);
            Assert.Equal("text/plain", fp.File.MimeType);
        }

        [Fact]
        public void ToPart_ConvertsDataContent()
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            var content = new DataContent(payload, "application/custom");
            var part = content.ToPart();
            var fp = Assert.IsType<FilePart>(part);
            Assert.NotNull(fp.File.Bytes);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Convert.FromBase64String(fp.File.Bytes));
            Assert.Equal("application/custom", fp.File.MimeType);
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
        private sealed class CustomPart() : Part("custom-kind") { }
    }
}
