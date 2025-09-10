using System.Text.Json;

namespace A2A.UnitTests.Models;

public class FileContentSpecComplianceTests
{
    [Fact]
    public void FileContent_Deserialize_WithoutKind_ShouldSucceed_ForBytesContent()
    {
        // Arrange: JSON according to A2A spec (without "kind" property)
        const string json = """
        {
            "name": "example.txt",
            "mimeType": "text/plain",
            "bytes": "SGVsbG8gV29ybGQ="
        }
        """;

        // Act & Assert: This should work according to A2A spec
        var fileContent = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(fileContent);
        Assert.IsType<FileWithBytes>(fileContent);
        var fileWithBytes = (FileWithBytes)fileContent;
        Assert.Equal("example.txt", fileWithBytes.Name);
        Assert.Equal("text/plain", fileWithBytes.MimeType);
        Assert.Equal("SGVsbG8gV29ybGQ=", fileWithBytes.Bytes);
    }

    [Fact]
    public void FileContent_Deserialize_WithoutKind_ShouldSucceed_ForUriContent()
    {
        // Arrange: JSON according to A2A spec (without "kind" property)
        const string json = """
        {
            "name": "example.txt",
            "mimeType": "text/plain",
            "uri": "https://example.com/file.txt"
        }
        """;

        // Act & Assert: This should work according to A2A spec
        var fileContent = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(fileContent);
        Assert.IsType<FileWithUri>(fileContent);
        var fileWithUri = (FileWithUri)fileContent;
        Assert.Equal("example.txt", fileWithUri.Name);
        Assert.Equal("text/plain", fileWithUri.MimeType);
        Assert.Equal("https://example.com/file.txt", fileWithUri.Uri);
    }

    [Fact]
    public void FileContent_Serialize_ShouldNotIncludeKind()
    {
        // Arrange
        var fileWithBytes = new FileWithBytes
        {
            Name = "test.txt",
            MimeType = "text/plain",
            Bytes = "SGVsbG8="
        };

        // Act
        var json = JsonSerializer.Serialize(fileWithBytes, A2AJsonUtilities.DefaultOptions);

        // Assert: Should not contain "kind" property
        Assert.DoesNotContain("\"kind\"", json);
        Assert.Contains("\"bytes\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"mimeType\"", json);
    }

    [Fact]
    public void FileContent_Serialize_UriContent_ShouldNotIncludeKind()
    {
        // Arrange
        var fileWithUri = new FileWithUri
        {
            Name = "test.txt",
            MimeType = "text/plain",
            Uri = "https://example.com/test.txt"
        };

        // Act
        var json = JsonSerializer.Serialize(fileWithUri, A2AJsonUtilities.DefaultOptions);

        // Assert: Should not contain "kind" property
        Assert.DoesNotContain("\"kind\"", json);
        Assert.Contains("\"uri\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"mimeType\"", json);
    }

    [Fact]
    public void FileContent_Deserialize_WithBothBytesAndUri_ShouldThrow()
    {
        // Arrange: Invalid JSON with both bytes and uri
        const string json = """
        {
            "name": "example.txt",
            "mimeType": "text/plain",
            "bytes": "SGVsbG8=",
            "uri": "https://example.com/file.txt"
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("cannot have both 'bytes' and 'uri'", ex.Message);
    }

    [Fact]
    public void FileContent_Deserialize_WithNeitherBytesNorUri_ShouldThrow()
    {
        // Arrange: Invalid JSON with neither bytes nor uri
        const string json = """
        {
            "name": "example.txt",
            "mimeType": "text/plain"
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("must have either 'bytes' or 'uri'", ex.Message);
    }

    [Fact]
    public void FileContent_RoundTrip_Bytes_ShouldWork()
    {
        // Arrange
        var original = new FileWithBytes
        {
            Name = "test.txt",
            MimeType = "text/plain",
            Bytes = "SGVsbG8gV29ybGQ=",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["key"] = JsonDocument.Parse("\"value\"").RootElement
            }
        };

        // Act: Serialize and deserialize
        var json = JsonSerializer.Serialize(original, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.IsType<FileWithBytes>(deserialized);
        var fileWithBytes = (FileWithBytes)deserialized;
        Assert.Equal(original.Name, fileWithBytes.Name);
        Assert.Equal(original.MimeType, fileWithBytes.MimeType);
        Assert.Equal(original.Bytes, fileWithBytes.Bytes);
        Assert.Single(fileWithBytes.Metadata);
        Assert.Equal("value", fileWithBytes.Metadata["key"].GetString());
    }

    [Fact]
    public void FileContent_RoundTrip_Uri_ShouldWork()
    {
        // Arrange
        var original = new FileWithUri
        {
            Name = "test.txt",
            MimeType = "text/plain",
            Uri = "https://example.com/test.txt",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["key"] = JsonDocument.Parse("42").RootElement
            }
        };

        // Act: Serialize and deserialize
        var json = JsonSerializer.Serialize(original, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.IsType<FileWithUri>(deserialized);
        var fileWithUri = (FileWithUri)deserialized;
        Assert.Equal(original.Name, fileWithUri.Name);
        Assert.Equal(original.MimeType, fileWithUri.MimeType);
        Assert.Equal(original.Uri, fileWithUri.Uri);
        Assert.Single(fileWithUri.Metadata);
        Assert.Equal(42, fileWithUri.Metadata["key"].GetInt32());
    }
}