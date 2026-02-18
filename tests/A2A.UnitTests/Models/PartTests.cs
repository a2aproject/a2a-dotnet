using System.Text.Json;

namespace A2A.UnitTests.Models;

public sealed class PartTests
{
    [Fact]
    public void FromText_SetsContentCaseToText()
    {
        var part = Part.FromText("hello");

        Assert.Equal(PartContentCase.Text, part.ContentCase);
        Assert.Equal("hello", part.Text);
    }

    [Fact]
    public void FromRaw_SetsContentCaseToRaw()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var part = Part.FromRaw(data, "application/octet-stream", "file.bin");

        Assert.Equal(PartContentCase.Raw, part.ContentCase);
        Assert.Equal(data, part.Raw);
        Assert.Equal("application/octet-stream", part.MediaType);
        Assert.Equal("file.bin", part.Filename);
    }

    [Fact]
    public void FromUrl_SetsContentCaseToUrl()
    {
        var part = Part.FromUrl("https://example.com/file.pdf", "application/pdf", "file.pdf");

        Assert.Equal(PartContentCase.Url, part.ContentCase);
        Assert.Equal("https://example.com/file.pdf", part.Url);
        Assert.Equal("application/pdf", part.MediaType);
        Assert.Equal("file.pdf", part.Filename);
    }

    [Fact]
    public void FromData_SetsContentCaseToData()
    {
        var element = JsonSerializer.SerializeToElement(new { key = "value" });
        var part = Part.FromData(element);

        Assert.Equal(PartContentCase.Data, part.ContentCase);
        Assert.NotNull(part.Data);
        Assert.Equal("value", part.Data.Value.GetProperty("key").GetString());
    }

    [Fact]
    public void ContentCase_WhenEmpty_ReturnsNone()
    {
        var part = new Part();

        Assert.Equal(PartContentCase.None, part.ContentCase);
    }

    [Fact]
    public void RoundTrip_TextPart_PreservesContent()
    {
        var part = Part.FromText("round trip text");

        var json = JsonSerializer.Serialize(part, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Part>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(PartContentCase.Text, deserialized.ContentCase);
        Assert.Equal("round trip text", deserialized.Text);
    }

    [Fact]
    public void RoundTrip_UrlPart_PreservesMediaTypeAndFilename()
    {
        var part = Part.FromUrl("https://example.com/doc.pdf", "application/pdf", "doc.pdf");

        var json = JsonSerializer.Serialize(part, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Part>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(PartContentCase.Url, deserialized.ContentCase);
        Assert.Equal("https://example.com/doc.pdf", deserialized.Url);
        Assert.Equal("application/pdf", deserialized.MediaType);
        Assert.Equal("doc.pdf", deserialized.Filename);
    }

    [Fact]
    public void RoundTrip_RawPart_PreservesBinaryData()
    {
        var rawData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var part = Part.FromRaw(rawData, "application/octet-stream", "data.bin");

        var json = JsonSerializer.Serialize(part, A2AJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<Part>(json, A2AJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(PartContentCase.Raw, deserialized.ContentCase);
        Assert.Equal(rawData, deserialized.Raw);
        Assert.Equal("application/octet-stream", deserialized.MediaType);
        Assert.Equal("data.bin", deserialized.Filename);
    }
}
