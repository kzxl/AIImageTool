using System;
using System.IO;
using ImageTool.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class IccProfileWriterTests
{
    [Theory]
    [InlineData(ColorSpaces.Space.Srgb)]
    [InlineData(ColorSpaces.Space.AdobeRgb)]
    [InlineData(ColorSpaces.Space.Rec2020)]
    [InlineData(ColorSpaces.Space.DisplayP3)]
    public void Build_RoundTrips_ThroughReader(ColorSpaces.Space space)
    {
        var icc = IccProfileWriter.Build(space);
        Assert.NotNull(icc);
        Assert.True(icc.Length > 132);

        // 'acsp' o offset 36 (ICC hop le).
        Assert.Equal((byte)'a', icc[36]);
        Assert.Equal((byte)'c', icc[37]);
        Assert.Equal((byte)'s', icc[38]);
        Assert.Equal((byte)'p', icc[39]);

        // Reader doc lai colorant matrix -> phai khop dung gamut da ghi.
        var matrix = IccProfileReader.TryReadRgbToXyzD65(icc);
        Assert.NotNull(matrix);
        var matched = ColorSpaces.MatchSpace(matrix!);
        Assert.Equal(space, matched);
    }

    [Fact]
    public void Build_Description_IsReadable()
    {
        var icc = IccProfileWriter.Build(ColorSpaces.Space.AdobeRgb);
        var desc = IccProfileReader.TryReadDescription(icc);
        Assert.NotNull(desc);
        Assert.Contains("Adobe RGB", desc);
        // Description-based detection cung nhan ra Adobe.
        Assert.Equal(ColorSpaces.Space.AdobeRgb, IccProfileReader.GuessSpace(desc));
    }

    [Fact]
    public void Build_SizeFieldMatchesActualLength()
    {
        var icc = IccProfileWriter.Build(ColorSpaces.Space.Srgb);
        int declared = (icc[0] << 24) | (icc[1] << 16) | (icc[2] << 8) | icc[3];
        Assert.Equal(icc.Length, declared);
    }

    [Theory]
    [InlineData(ColorSpaces.Space.Srgb, "png")]
    [InlineData(ColorSpaces.Space.AdobeRgb, "png")]
    [InlineData(ColorSpaces.Space.DisplayP3, "jpg")]
    [InlineData(ColorSpaces.Space.Rec2020, "jpg")]
    public void Embed_SurvivesSaveLoad_AndDetectsGamut(ColorSpaces.Space space, string ext)
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_iccw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "t." + ext);
            using (var img = new Image<Rgba32>(8, 8, new Rgba32(120, 130, 140, 255)))
            {
                img.Metadata.IccProfile = new IccProfile(IccProfileWriter.Build(space));
                if (ext == "png") img.SaveAsPng(path); else img.SaveAsJpeg(path);
            }
            var detected = IccProfileReader.DetectSpaceFromFile(path);
            Assert.Equal(space, detected);
        }
        finally { Directory.Delete(dir, true); }
    }
}
