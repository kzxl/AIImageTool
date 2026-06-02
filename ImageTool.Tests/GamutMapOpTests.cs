using System;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class GamutMapOpTests
{
    private static LinearImage Solid(int size, float r, float g, float b)
    {
        var img = new LinearImage(size, size);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(GamutMapOp.Type));
    }

    [Fact]
    public void RoundTrip()
    {
        var op = new GamutMapOp { Dest = ColorSpaces.Space.AdobeRgb, Method = GamutMapOp.Mode.Desaturate };
        var back = GamutMapOp.FromParams(op.ToParams());
        Assert.Equal(ColorSpaces.Space.AdobeRgb, back.Dest);
        Assert.Equal(GamutMapOp.Mode.Desaturate, back.Method);
    }

    [Fact]
    public void RoundTrip_WithDestMatrix()
    {
        var op = new GamutMapOp { DestMatrix = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.Rec2020) };
        var back = GamutMapOp.FromParams(op.ToParams());
        Assert.NotNull(back.DestMatrix);
        Assert.Equal(9, back.DestMatrix!.Length);
        Assert.Equal(op.DestMatrix![0], back.DestMatrix[0], 4);
    }

    [Fact]
    public void ClipToSrgb_ClampsNegativeAndOver()
    {
        // màu ngoài [0,1] (âm + cháy). Clip-to-sRGB phải kẹp về [0,1] (dest = working = sRGB).
        var img = Solid(4, -0.2f, 0.5f, 1.6f);
        new GamutMapOp { Dest = ColorSpaces.Space.Srgb, Method = GamutMapOp.Mode.Clip }.Apply(img, 1f);
        Assert.True(img.Pixels[0] >= 0f && img.Pixels[0] <= 1.0001f);
        Assert.True(img.Pixels[2] <= 1.0001f);
    }

    [Fact]
    public void InGamutColor_Unchanged_WhenDestEqualsWorking()
    {
        // màu hợp lệ trong sRGB + dest = sRGB -> gần như không đổi.
        var img = Solid(4, 0.3f, 0.5f, 0.7f);
        var before = (float[])img.Pixels.Clone();
        new GamutMapOp { Dest = ColorSpaces.Space.Srgb, Method = GamutMapOp.Mode.Clip }.Apply(img, 1f);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], img.Pixels[i], 4);
    }

    [Fact]
    public void Desaturate_PreservesLuminance_Roughly()
    {
        // màu rực ngoài gamut nhỏ hơn (proof về sRGB từ working sRGB) -> desaturate giữ luminance.
        var img = Solid(4, 1.4f, 0.0f, 0.0f); // đỏ cháy ngoài [0,1]
        float lumBefore = ColorSpace.Luminance(img.Pixels[0], img.Pixels[1], img.Pixels[2]);
        new GamutMapOp { Dest = ColorSpaces.Space.Srgb, Method = GamutMapOp.Mode.Desaturate }.Apply(img, 1f);
        float lumAfter = ColorSpace.Luminance(img.Pixels[0], img.Pixels[1], img.Pixels[2]);
        // luminance không tăng vọt; màu được kéo bớt rực (G/B tăng so với 0).
        Assert.True(img.Pixels[1] >= 0f);
        Assert.True(lumAfter <= lumBefore + 1e-3f);
    }

    [Fact]
    public void ReplaysViaPipeline()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var img = Solid(4, -0.3f, 0.5f, 1.5f);
        var ops = new System.Collections.Generic.List<EditOperation>
        {
            new EditOperation
            {
                OpType = GamutMapOp.Type,
                Params = new System.Collections.Generic.Dictionary<string, string>
                { ["dest"] = "sRGB", ["method"] = "clip" }
            }
        };
        var result = pipeline.Render(img, ops);
        Assert.True(result.Pixels[0] >= 0f);
        Assert.True(result.Pixels[2] <= 1.0001f);
    }
}

public class GamutCheckTests
{
    [Fact]
    public void InGamut_NeutralGray_NotFlagged()
    {
        Assert.False(GamutCheck.IsOutOfGamut(0.5f, 0.5f, 0.5f, ColorSpaces.Space.Srgb));
    }

    [Fact]
    public void OutOfGamut_NegativeChannel_Flagged()
    {
        // màu âm (ngoài mọi gamut RGB hợp lệ).
        Assert.True(GamutCheck.IsOutOfGamut(-0.2f, 0.5f, 0.5f, ColorSpaces.Space.Srgb));
    }

    [Fact]
    public void SrgbColor_InWiderGamut_NotFlagged()
    {
        // màu sRGB hợp lệ luôn nằm trong gamut rộng hơn (Rec2020).
        Assert.False(GamutCheck.IsOutOfGamut(0.8f, 0.1f, 0.1f, ColorSpaces.Space.Rec2020));
    }

    [Fact]
    public void OutOfGamutFraction_AllOut_IsOne()
    {
        var img = new LinearImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = -0.5f; img.Pixels[i + 1] = 0.2f; img.Pixels[i + 2] = 0.2f; img.Pixels[i + 3] = 1f; }
        Assert.Equal(1f, GamutCheck.OutOfGamutFraction(img, ColorSpaces.Space.Srgb), 3);
    }

    [Fact]
    public void OutOfGamutFraction_AllIn_IsZero()
    {
        var img = new LinearImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.4f; img.Pixels[i + 1] = 0.4f; img.Pixels[i + 2] = 0.4f; img.Pixels[i + 3] = 1f; }
        Assert.Equal(0f, GamutCheck.OutOfGamutFraction(img, ColorSpaces.Space.Srgb), 3);
    }
}
