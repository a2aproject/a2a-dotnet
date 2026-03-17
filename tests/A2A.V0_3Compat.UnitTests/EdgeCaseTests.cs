using System.Text.Json;
using V03 = A2A.V0_3;

namespace A2A.V0_3Compat.UnitTests;

public class V03FilePartEdgeCaseTests
{
    /// <summary>
    /// Edge case 1: When a FilePart.File object exists but has neither Bytes nor Uri set.
    /// This happens when FileContent is created via parameterless constructor (for deserialization).
    /// Line 198 in V03TypeConverter.cs catches this with a fallback to an empty Part.
    /// </summary>
    [Fact]
    public void ToV1Part_WithFilePartHavingEmptyFileContent_ReturnsEmptyPart()
    {
        // Arrange: FilePart with File that has neither Bytes nor Uri
        var emptyFileContent = new V03.FileContent();  // Parameterless constructor - no content set
        var filePart = new V03.FilePart { File = emptyFileContent };

        // Act
        var v1Part = V03TypeConverter.ToV1Part(filePart);

        // Assert: Should match pattern on line 198 (neither bytes nor uri)
        Assert.Equal(PartContentCase.None, v1Part.ContentCase);
        Assert.Null(v1Part.Text);
        Assert.Null(v1Part.Raw);
        Assert.Null(v1Part.Url);
        Assert.Null(v1Part.Data);
    }

    /// <summary>
    /// Edge case 2: Convert.FromBase64String throws FormatException on line 186
    /// when Bytes contains invalid base64 data.
    /// The converter does not currently catch this exception, so it propagates to the caller.
    /// </summary>
    [Fact]
    public void ToV1Part_WithInvalidBase64_ThrowsFormatException()
    {
        // Arrange: FilePart with invalid base64 string
        var invalidBase64FilePart = new V03.FilePart
        {
            File = new V03.FileContent("!!!INVALID_BASE64!!!")
            {
                MimeType = "text/plain"
            }
        };

        // Act & Assert: FormatException is thrown on line 186
        var ex = Assert.Throws<FormatException>(() => V03TypeConverter.ToV1Part(invalidBase64FilePart));
        Assert.Contains("not a valid Base64", ex.Message);
    }

    /// <summary>
    /// Edge case 3: When part.Raw is an empty byte array on line 70.
    /// Empty bytes (byte[]{}) converts to an empty base64 string ("").
    /// This is valid and should be preserved in round-trip conversion.
    /// </summary>
    [Fact]
    public void ToV03Part_WithEmptyByteArray_ProducesEmptyBase64()
    {
        // Arrange: Part with empty byte array
        var emptyBytes = System.Array.Empty<byte>();
        var v1Part = Part.FromRaw(emptyBytes, "application/octet-stream", "empty.bin");

        // Act
        var v03Part = V03TypeConverter.ToV03Part(v1Part);

        // Assert: Empty array converts to empty base64 string
        var filePart = Assert.IsType<V03.FilePart>(v03Part);
        Assert.NotNull(filePart.File);
        Assert.Equal(string.Empty, filePart.File.Bytes);  // base64("") = ""
        Assert.Null(filePart.File.Uri);
        Assert.Equal("application/octet-stream", filePart.File.MimeType);
        Assert.Equal("empty.bin", filePart.File.Name);
    }

    /// <summary>
    /// Edge case 4: When part.Url is an invalid URL string on line 75.
    /// The Uri constructor is called on line 75, which throws UriFormatException
    /// if the URL cannot be parsed.
    /// </summary>
    [Fact]
    public void ToV03Part_WithInvalidUrlString_ThrowsUriFormatException()
    {
        // Arrange: Part with an invalid URL that can't be parsed as Uri
        var invalidUrl = "not://a/valid:url@@@";
        var v1Part = Part.FromUrl(invalidUrl, "text/plain", "file.txt");

        // Act & Assert: UriFormatException is thrown on line 75
        var ex = Assert.Throws<UriFormatException>(() => V03TypeConverter.ToV03Part(v1Part));
    }

    /// <summary>
    /// Full round-trip test: v1 → v0.3 → v1 (Raw/Bytes)
    /// Verifies that binary data, metadata, and filenames survive conversion both ways.
    /// </summary>
    [Fact]
    public void RoundTrip_RawPart_PreservesDataAndMetadata()
    {
        // Arrange: Create v1 Part with Raw data
        var originalBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var metadata = new Dictionary<string, JsonElement>
        {
            ["version"] = JsonSerializer.SerializeToElement("1.0")
        };
        var v1Part1 = Part.FromRaw(originalBytes, "application/octet-stream", "binary.bin");
        v1Part1.Metadata = metadata;

        // Act: Convert v1 → v0.3
        var v03Part = V03TypeConverter.ToV03Part(v1Part1);

        // Assert after first conversion
        var filePart = Assert.IsType<V03.FilePart>(v03Part);
        Assert.Equal(Convert.ToBase64String(originalBytes), filePart.File.Bytes);
        Assert.Equal("application/octet-stream", filePart.File.MimeType);
        Assert.Equal("binary.bin", filePart.File.Name);

        // Act: Convert v0.3 → v1
        var v1Part2 = V03TypeConverter.ToV1Part(v03Part);

        // Assert round-trip: Should match original
        Assert.Equal(PartContentCase.Raw, v1Part2.ContentCase);
        Assert.Equal(originalBytes, v1Part2.Raw);
        Assert.Equal("application/octet-stream", v1Part2.MediaType);
        Assert.Equal("binary.bin", v1Part2.Filename);
    }

    /// <summary>
    /// Full round-trip test: v1 → v0.3 → v1 (URL/Uri)
    /// Verifies that URLs survive conversion both ways.
    /// </summary>
    [Fact]
    public void RoundTrip_UrlPart_PreservesUriAndMetadata()
    {
        // Arrange: Create v1 Part with URL
        var url = "https://example.com/resource.pdf";
        var v1Part1 = Part.FromUrl(url, "application/pdf", "resource.pdf");

        // Act: Convert v1 → v0.3
        var v03Part = V03TypeConverter.ToV03Part(v1Part1);

        // Assert after first conversion
        var filePart = Assert.IsType<V03.FilePart>(v03Part);
        Assert.Equal(new Uri(url), filePart.File.Uri);
        Assert.Null(filePart.File.Bytes);
        Assert.Equal("application/pdf", filePart.File.MimeType);

        // Act: Convert v0.3 → v1
        var v1Part2 = V03TypeConverter.ToV1Part(v03Part);

        // Assert round-trip: Should match original
        Assert.Equal(PartContentCase.Url, v1Part2.ContentCase);
        Assert.Equal(url, v1Part2.Url);
        Assert.Equal("application/pdf", v1Part2.MediaType);
        Assert.Equal("resource.pdf", v1Part2.Filename);
    }

    /// <summary>
    /// Tests preservation of metadata through round-trip conversion.
    /// Metadata should be preserved even when content type changes or is empty.
    /// </summary>
    [Fact]
    public void ToV1Part_WithMetadataButEmptyContent_ReturnsPartWithMetadata()
    {
        // Arrange: FilePart with metadata but empty FileContent
        var metadata = new Dictionary<string, JsonElement>
        {
            ["info"] = JsonSerializer.SerializeToElement("metadata only")
        };
        var emptyFileContent = new V03.FileContent();
        var filePart = new V03.FilePart
        {
            File = emptyFileContent,
            Metadata = metadata,
        };

        // Act
        var v1Part = V03TypeConverter.ToV1Part(filePart);

        // Assert
        Assert.Equal(PartContentCase.None, v1Part.ContentCase);
        Assert.NotNull(v1Part.Metadata);
        Assert.True(v1Part.Metadata!.ContainsKey("info"));
        Assert.Equal("metadata only", v1Part.Metadata["info"].GetString());
    }
}
