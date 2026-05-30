using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ToneD1Tests
{
    private static LinearImage Solid(float v, int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // ---- Sigmoid (D1.1) ----

    [Fact]
    public void Sigmoid_Identity_WhenAmountZero()
    {
        var op = new SigmoidOp { Amount = 0 };
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void Sigmoid_CompressesHighlight()
    {
        // highlight vượt 1.0 -> sigmoid kéo về <=1.
        var img = Solid(4.0f);
        new SigmoidOp { Amount = 1f, Contrast = 1.5f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] <= 1.01f);
    }

    [Fact]
    public void Sigmoid_PivotMapsToMid()
    {
        // tại pivot (0.18), luminance-mode giữ tỉ lệ; perChannel sigmoid(pivot) ~ 0.5.
        var img = Solid(0.18f);
        new SigmoidOp { Amount = 1f, Contrast = 1.5f, Pivot = 0.18f, PerChannel = true }.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.45f, 0.55f);
    }

    [Fact]
    public void Sigmoid_RoundTrip()
    {
        var op = new SigmoidOp { Amount = 0.8f, Contrast = 2f, Pivot = 0.2f, PerChannel = true };
        var back = SigmoidOp.FromParams(op.ToParams());
        Assert.Equal(0.8f, back.Amount, 4);
        Assert.Equal(2f, back.Contrast, 4);
        Assert.True(back.PerChannel);
    }

    // ---- FilmicRgb (D1.2) ----

    [Fact]
    public void FilmicRgb_Identity_WhenAmountZero()
    {
        Assert.True(new FilmicRgbOp { Amount = 0 }.IsIdentity);
    }

    [Fact]
    public void FilmicRgb_CompressesHighlight()
    {
        var img = Solid(8.0f);
        new FilmicRgbOp { Amount = 1f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] <= 1.01f);
    }

    [Fact]
    public void FilmicRgb_Monotonic_DarkerStaysDarker()
    {
        var dark = Solid(0.1f); var bright = Solid(0.6f);
        var op = new FilmicRgbOp { Amount = 1f };
        op.Apply(dark, 1f); op.Apply(bright, 1f);
        Assert.True(bright.Pixels[0] > dark.Pixels[0]);
    }

    [Fact]
    public void FilmicRgb_RoundTrip()
    {
        var op = new FilmicRgbOp { Amount = 1f, WhiteRelative = 5f, BlackRelative = -7f, Contrast = 1.3f, Latitude = 0.25f, Saturation = 0.2f };
        var back = FilmicRgbOp.FromParams(op.ToParams());
        Assert.Equal(5f, back.WhiteRelative, 3);
        Assert.Equal(-7f, back.BlackRelative, 3);
        Assert.Equal(0.25f, back.Latitude, 3);
    }

    // ---- ToneEqualizer (D1.3) ----

    [Fact]
    public void ToneEq_Identity_WhenAllZero()
    {
        Assert.True(new ToneEqualizerOp().IsIdentity);
    }

    [Fact]
    public void ToneEq_ShadowsLift_BrightensDarkImage()
    {
        var img = Solid(ColorSpace.SrgbToLinear(0.15f)); // ảnh tối -> rơi vào zone shadows
        float before = img.Pixels[0];
        new ToneEqualizerOp { Shadows = 1f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] > before);
    }

    [Fact]
    public void ToneEq_HighlightsLower_DarkensBrightImage()
    {
        var img = Solid(ColorSpace.SrgbToLinear(0.85f)); // sáng -> zone highlights
        float before = img.Pixels[0];
        new ToneEqualizerOp { Highlights = -1f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] < before);
    }

    [Fact]
    public void ToneEq_RoundTrip_AndRegistered()
    {
        var op = new ToneEqualizerOp { Blacks = -0.5f, Midtones = 0.3f, Whites = 0.2f };
        var back = ToneEqualizerOp.FromParams(op.ToParams());
        Assert.Equal(-0.5f, back.Blacks, 4);
        Assert.Equal(0.3f, back.Midtones, 4);
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(SigmoidOp.Type));
        Assert.True(reg.Has(FilmicRgbOp.Type));
        Assert.True(reg.Has(ToneEqualizerOp.Type));
    }
}
