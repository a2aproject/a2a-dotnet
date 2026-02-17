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
            "mediaType": "text/plain",
            "fileWithBytes": "SGVsbG8gV29ybGQ="
        }
        """;

        // Act & Assert: This should work according to A2A spec
        var fileContent = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(fileContent);
        Assert.Equal("example.txt", fileContent.Name);
        Assert.Equal("text/plain", fileContent.MediaType);
        Assert.Equal("SGVsbG8gV29ybGQ=", fileContent.FileWithBytes);
    }

    [Fact]
    public void FileContent_Deserialize_WithoutKind_ShouldSucceed_ForUriContent()
    {
        // Arrange: JSON according to A2A spec (without "kind" property)
        const string json = """
        {
            "name": "example.txt",
            "mediaType": "text/plain",
            "fileWithUri": "https://example.com/file.txt"
        }
        """;

        // Act & Assert: This should work according to A2A spec
        var fileContent = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(fileContent);
        Assert.Equal("example.txt", fileContent.Name);
        Assert.Equal("text/plain", fileContent.MediaType);
        Assert.NotNull(fileContent.FileWithUri);
        Assert.Equal("https://example.com/file.txt", fileContent.FileWithUri.ToString());
    }

    [Fact]
    public void FileContent_Serialize_ShouldNotIncludeKind()
    {
        // Arrange
        var fileWithBytes = new FileContent("SGVsbG8=")
        {
            Name = "test.txt",
            MediaType = "text/plain",
        };

        // Act
        var json = JsonSerializer.Serialize(fileWithBytes, A2AJsonUtilities.DefaultOptions);

        // Assert: Should not contain "kind" property
        Assert.DoesNotContain("\"kind\"", json);
        Assert.Contains("\"fileWithBytes\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"mediaType\"", json);
    }

    [Fact]
    public void FileContent_Serialize_UriContent_ShouldNotIncludeKind()
    {
        // Arrange
        var fileWithUri = new FileContent(new Uri("https://example.com/test.txt"))
        {
            Name = "test.txt",
            MediaType = "text/plain",
        };

        // Act
        var json = JsonSerializer.Serialize(fileWithUri, A2AJsonUtilities.DefaultOptions);

        // Assert: Should not contain "kind" property
        Assert.DoesNotContain("\"kind\"", json);
        Assert.Contains("\"fileWithUri\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"mediaType\"", json);
    }

    [Fact]
    public void FileContent_Deserialize_WithBothBytesAndUri_ShouldThrow()
    {
        // Arrange: Invalid JSON with both fileWithBytes and fileWithUri
        const string json = """
        {
            "name": "example.txt",
            "mediaType": "text/plain",
            "fileWithBytes": "SGVsbG8=",
            "fileWithUri": "https://example.com/file.txt"
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("Only one of 'fileWithBytes' or 'fileWithUri' must be specified", ex.Message);
    }

    [Fact]
    public void FileContent_Deserialize_WithNeitherBytesNorUri_ShouldThrow()
    {
        // Arrange: Invalid JSON with neither fileWithBytes nor fileWithUri
        const string json = """
        {
            "name": "example.txt",
            "mediaType": "text/plain"
        }
        """;

        // Act & Assert
        var ex = Assert.Throws<A2AException>(() =>
            JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions));

        Assert.Equal(A2AErrorCode.InvalidRequest, ex.ErrorCode);
        Assert.Contains("must have either 'fileWithBytes' or 'fileWithUri'", ex.Message);
    }

    [Fact]
    public void FileContent_RoundTrip_Bytes_ShouldWork()
    {
        // Arrange
        var original = new FileContent("SGVsbG8gV29ybGQ=")
        {
            Name = "test.txt",
            MediaType = "text/plain",
        };

        // Act: Serialize and deserialize
        var json = JsonSerializer.Serialize(original, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.MediaType, deserialized.MediaType);
        Assert.Equal(original.FileWithBytes, deserialized.FileWithBytes);
    }

    [Fact]
    public void FileContent_RoundTrip_Uri_ShouldWork()
    {
        // Arrange
        var original = new FileContent(new Uri("https://example.com/test.txt"))
        {
            Name = "test.txt",
            MediaType = "text/plain",
        };

        // Act: Serialize and deserialize
        var json = JsonSerializer.Serialize(original, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.MediaType, deserialized.MediaType);
        Assert.Equal(original.FileWithUri, deserialized.FileWithUri);
    }
}
