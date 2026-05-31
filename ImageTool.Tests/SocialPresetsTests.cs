using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class SocialPresetsTests
{
    [Fact]
    public void All_HasCommonPlatforms()
    {
        Assert.Contains(SocialPresets.All, p => p.Name.Contains("Instagram Square"));
        Assert.Contains(SocialPresets.All, p => p.Name.Contains("YouTube Thumbnail"));
        Assert.Contains(SocialPresets.All, p => p.Name.Contains("Story"));
    }

    [Fact]
    public void All_DimensionsPositive()
    {
        foreach (var p in SocialPresets.All)
        {
            Assert.True(p.Width > 0, p.Name);
            Assert.True(p.Height > 0, p.Name);
        }
    }

    [Fact]
    public void InstagramSquare_Is1080()
    {
        var ig = System.Array.Find(SocialPresets.All, p => p.Name.Contains("Instagram Square"))!;
        Assert.Equal(1080, ig.Width);
        Assert.Equal(1080, ig.Height);
    }

    [Fact]
    public void ToJobParams_SetsExactSizeAndWebDefaults()
    {
        var yt = System.Array.Find(SocialPresets.All, p => p.Name.Contains("YouTube"))!;
        var prm = SocialPresets.ToJobParams(yt, @"C:\out", "{name}.{ext}");

        Assert.Equal("1280", prm["exactWidth"]);
        Assert.Equal("720", prm["exactHeight"]);
        Assert.Equal("srgb", prm["outputProfile"]);   // sRGB cho web
        Assert.Equal("true", prm["stripMetadata"]);    // strip metadata
        Assert.Equal("jpg", prm["format"]);
        Assert.Equal(@"C:\out", prm["outDir"]);
    }
}
