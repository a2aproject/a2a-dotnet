using System.Text.Json;

namespace A2A.V0_3.UnitTests.Models;

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
        Assert.Equal("example.txt", fileContent.Name);
        Assert.Equal("text/plain", fileContent.MimeType);
        Assert.Equal("SGVsbG8gV29ybGQ=", fileContent.Bytes);
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
        Assert.Equal("example.txt", fileContent.Name);
        Assert.Equal("text/plain", fileContent.MimeType);
        Assert.NotNull(fileContent.Uri);
        Assert.Equal("https://example.com/file.txt", fileContent.Uri.ToString());
    }

    [Fact]
    public void FileContent_Serialize_ShouldNotIncludeKind()
    {
        // Arrange
        var fileWithBytes = new FileContent("SGVsbG8=")
        {
            Name = "test.txt",
            MimeType = "text/plain",
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
        var fileWithUri = new FileContent(new Uri("https://example.com/test.txt"))
        {
            Name = "test.txt",
            MimeType = "text/plain",
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
        Assert.Contains("Only one of 'bytes' or 'uri' must be specified", ex.Message);
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
        var original = new FileContent("SGVsbG8gV29ybGQ=")
        {
            Name = "test.txt",
            MimeType = "text/plain",
        };

        // Act: Serialize and deserialize
        var json = JsonSerializer.Serialize(original, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.MimeType, deserialized.MimeType);
        Assert.Equal(original.Bytes, deserialized.Bytes);
    }

    [Fact]
    public void FileContent_RoundTrip_Uri_ShouldWork()
    {
        // Arrange
        var original = new FileContent(new Uri("https://example.com/test.txt"))
        {
            Name = "test.txt",
            MimeType = "text/plain",
        };

        // Act: Serialize and deserialize
        var json = JsonSerializer.Serialize(original, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FileContent>(json, A2AJsonUtilities.DefaultOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.MimeType, deserialized.MimeType);
        Assert.Equal(original.Uri, deserialized.Uri);
    }
}
