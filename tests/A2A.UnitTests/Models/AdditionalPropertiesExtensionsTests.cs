using System.Text.Json;
using Microsoft.Extensions.AI;

namespace A2A.UnitTests.Models
{
    public sealed class AdditionalPropertiesExtensionsTests
    {
        [Fact]
        public void ToAdditionalProperties_ReturnsNullForNullMetadata()
        {
            Dictionary<string, JsonElement>? metadata = null;
            var result = metadata.ToAdditionalProperties();
            Assert.Null(result);
        }

        [Fact]
        public void ToAdditionalProperties_ReturnsNullForEmptyMetadata()
        {
            var metadata = new Dictionary<string, JsonElement>();
            var result = metadata.ToAdditionalProperties();
            Assert.Null(result);
        }

        [Fact]
        public void ToAdditionalProperties_ConvertsMetadataToAdditionalProperties()
        {
            var metadata = new Dictionary<string, JsonElement>
            {
                ["num"] = JsonSerializer.SerializeToElement(42),
                ["str"] = JsonSerializer.SerializeToElement("value"),
                ["bool"] = JsonSerializer.SerializeToElement(true)
            };

            var result = metadata.ToAdditionalProperties();

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.True(result.TryGetValue("num", out var numObj));
            Assert.True(result.TryGetValue("str", out var strObj));
            Assert.True(result.TryGetValue("bool", out var boolObj));
            var numJe = Assert.IsType<JsonElement>(numObj);
            var strJe = Assert.IsType<JsonElement>(strObj);
            var boolJe = Assert.IsType<JsonElement>(boolObj);
            Assert.Equal(42, numJe.GetInt32());
            Assert.Equal("value", strJe.GetString());
            Assert.True(boolJe.GetBoolean());
        }

        [Fact]
        public void ToA2AMetadata_ReturnsNullForNullAdditionalProperties()
        {
            AdditionalPropertiesDictionary? additionalProperties = null;
            var result = additionalProperties.ToA2AMetadata();
            Assert.Null(result);
        }

        [Fact]
        public void ToA2AMetadata_ReturnsNullForEmptyAdditionalProperties()
        {
            var additionalProperties = new AdditionalPropertiesDictionary();
            var result = additionalProperties.ToA2AMetadata();
            Assert.Null(result);
        }

        [Fact]
        public void ToA2AMetadata_ConvertsAdditionalPropertiesToMetadata()
        {
            var additionalProperties = new AdditionalPropertiesDictionary
            {
                ["num"] = 42,
                ["str"] = "value",
                ["bool"] = true
            };

            var result = additionalProperties.ToA2AMetadata();

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.True(result.TryGetValue("num", out var numJe));
            Assert.True(result.TryGetValue("str", out var strJe));
            Assert.True(result.TryGetValue("bool", out var boolJe));
            Assert.Equal(42, numJe.GetInt32());
            Assert.Equal("value", strJe.GetString());
            Assert.True(boolJe.GetBoolean());
        }

        [Fact]
        public void ToA2AMetadata_HandlesJsonElementValues()
        {
            var additionalProperties = new AdditionalPropertiesDictionary
            {
                ["nested"] = JsonSerializer.SerializeToElement(new { key = "val" })
            };

            var result = additionalProperties.ToA2AMetadata();

            Assert.NotNull(result);
            Assert.Single(result!);
            Assert.True(result.TryGetValue("nested", out var nestedJe));
            Assert.Equal(JsonValueKind.Object, nestedJe.ValueKind);
            Assert.Equal("val", nestedJe.GetProperty("key").GetString());
        }

        [Fact]
        public void RoundTrip_ToA2AMetadata_And_ToAdditionalProperties()
        {
            var originalProps = new AdditionalPropertiesDictionary
            {
                ["number"] = 123,
                ["text"] = "hello"
            };

            var metadata = originalProps.ToA2AMetadata();
            Assert.NotNull(metadata);

            var restoredProps = metadata.ToAdditionalProperties();
            Assert.NotNull(restoredProps);
            Assert.Equal(2, restoredProps!.Count);

            // Values are now JsonElements after the round trip
            Assert.True(restoredProps.TryGetValue("number", out var numObj));
            Assert.True(restoredProps.TryGetValue("text", out var textObj));
            var numJe = Assert.IsType<JsonElement>(numObj);
            var textJe = Assert.IsType<JsonElement>(textObj);
            Assert.Equal(123, numJe.GetInt32());
            Assert.Equal("hello", textJe.GetString());
        }
    }
}
