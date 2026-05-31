using System;
using ImageTool.Core;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class ExifAutoTaggerTests
{
    [Fact]
    public void Generate_CameraHierarchy()
    {
        var img = new CatalogImage { CameraMake = "Canon", CameraModel = "EOS R5" };
        var tags = ExifAutoTagger.Generate(img);
        Assert.Contains("Camera/Canon", tags);
        Assert.Contains("Camera/Canon/EOS R5", tags);
    }

    [Fact]
    public void Generate_Lens()
    {
        var img = new CatalogImage { LensModel = "RF 24-70mm F2.8" };
        var tags = ExifAutoTagger.Generate(img);
        Assert.Contains("Lens/RF 24-70mm F2.8", tags);
    }

    [Fact]
    public void Generate_IsoAndFocalBuckets()
    {
        var img = new CatalogImage { Iso = 1250, FocalLength = 50 };
        var tags = ExifAutoTagger.Generate(img);
        Assert.Contains("ISO/800-1600", tags);
        Assert.Contains("Focal/Normal (35-70mm)", tags);
    }

    [Fact]
    public void Generate_DateHierarchy()
    {
        var img = new CatalogImage { DateTaken = new DateTime(2026, 3, 15) };
        var tags = ExifAutoTagger.Generate(img);
        Assert.Contains("Date/2026", tags);
        Assert.Contains("Date/2026/03", tags);
    }

    [Fact]
    public void Generate_EmptyMetadata_NoTags()
    {
        var tags = ExifAutoTagger.Generate(new CatalogImage());
        Assert.Empty(tags);
    }

    [Theory]
    [InlineData(50, "100")]
    [InlineData(100, "100")]
    [InlineData(160, "100-200")]
    [InlineData(1250, "800-1600")]
    [InlineData(12800, "6400+")]
    public void IsoBucket_Ranges(int iso, string expected)
        => Assert.Equal(expected, ExifAutoTagger.IsoBucket(iso));

    [Theory]
    [InlineData(14, "Ultra-wide (<16mm)")]
    [InlineData(24, "Wide (16-35mm)")]
    [InlineData(50, "Normal (35-70mm)")]
    [InlineData(200, "Tele (135-300mm)")]
    [InlineData(600, "Super-tele (300mm+)")]
    public void FocalBucket_Ranges(double mm, string expected)
        => Assert.Equal(expected, ExifAutoTagger.FocalBucket(mm));

    [Fact]
    public void Generate_SanitizesSlashesInModel()
    {
        var img = new CatalogImage { CameraMake = "X/Y", CameraModel = "A/B" };
        var tags = ExifAutoTagger.Generate(img);
        // '/' trong giá trị bị thay thành '-' để không phá cấu trúc phân cấp.
        Assert.Contains("Camera/X-Y", tags);
        Assert.Contains("Camera/X-Y/A-B", tags);
    }
}
