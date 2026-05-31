using System;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class UrlImageImporterTests
{
    [Theory]
    [InlineData("https://example.com/photo.jpg", true)]
    [InlineData("http://example.com/a/b/c.png", true)]
    [InlineData("HTTPS://EXAMPLE.COM/X.WEBP", true)]
    [InlineData("file:///C:/secret.jpg", false)]
    [InlineData("ftp://server/x.png", false)]
    [InlineData("data:image/png;base64,iVBOR", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidImageUrl_OnlyHttpHttps(string? url, bool expected)
    {
        Assert.Equal(expected, UrlImageImporter.IsValidImageUrl(url, out _));
    }

    [Fact]
    public void ResolveFileName_KeepsImageExtension()
    {
        var uri = new Uri("https://example.com/path/sunset.png");
        Assert.Equal("sunset.png", UrlImageImporter.ResolveFileName(uri, "image/png"));
    }

    [Fact]
    public void ResolveFileName_InfersExtFromContentType_WhenMissing()
    {
        var uri = new Uri("https://example.com/download?id=123");
        var name = UrlImageImporter.ResolveFileName(uri, "image/webp");
        Assert.EndsWith(".webp", name);
    }

    [Fact]
    public void ResolveFileName_DefaultsToJpg()
    {
        var uri = new Uri("https://example.com/img");
        var name = UrlImageImporter.ResolveFileName(uri, null);
        Assert.EndsWith(".jpg", name);
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("text/html", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData(null, false)]
    public void IsImageContentType_Works(string? ct, bool expected)
    {
        Assert.Equal(expected, UrlImageImporter.IsImageContentType(ct));
    }
}
