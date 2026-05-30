using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ColorD2Tests
{
    private static LinearImage Solid(float r, float g, float b, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // ---- RgbLevels (D2.5) ----

    [Fact]
    public void Levels_Identity()
    {
        Assert.True(new RgbLevelsOp().IsIdentity);
    }

    [Fact]
    public void Levels_BlackPoint_DarkensLows()
    {
        var img = Solid(ColorSpace.SrgbToLinear(0.2f), ColorSpace.SrgbToLinear(0.2f), ColorSpace.SrgbToLinear(0.2f));
        new RgbLevelsOp { Black = 0.1f }.Apply(img, 1f);
        // nâng điểm đen lên 0.1 -> giá trị 0.2 sRGB bị kéo tối hơn.
        Assert.True(ColorSpace.LinearToSrgb(img.Pixels[0]) < 0.2f);
    }

    [Fact]
    public void Levels_Gamma_BrightensMid()
    {
        var img = Solid(ColorSpace.SrgbToLinear(0.5f), ColorSpace.SrgbToLinear(0.5f), ColorSpace.SrgbToLinear(0.5f));
        new RgbLevelsOp { Gamma = 2f }.Apply(img, 1f);
        Assert.True(ColorSpace.LinearToSrgb(img.Pixels[0]) > 0.5f);
    }

    // ---- Velvia (D2.3) ----

    [Fact]
    public void Velvia_Identity_WhenZero()
    {
        Assert.True(new VelviaOp { Amount = 0 }.IsIdentity);
    }

    [Fact]
    public void Velvia_IncreasesSaturation()
    {
        var img = Solid(0.6f, 0.4f, 0.4f); // hơi đỏ
        float satBefore = (0.6f - 0.4f);
        new VelviaOp { Amount = 1f }.Apply(img, 1f);
        float mx = MathF.Max(img.Pixels[0], MathF.Max(img.Pixels[1], img.Pixels[2]));
        float mn = MathF.Min(img.Pixels[0], MathF.Min(img.Pixels[1], img.Pixels[2]));
        Assert.True(mx - mn > satBefore);
    }

    [Fact]
    public void Velvia_GrayUnchanged()
    {
        var img = Solid(0.5f, 0.5f, 0.5f);
        new VelviaOp { Amount = 1f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.49f, 0.51f);
    }

    // ---- ColorBalanceRgb (D2.1) ----

    [Fact]
    public void ColorBalance_Identity()
    {
        Assert.True(new ColorBalanceRgbOp().IsIdentity);
    }

    [Fact]
    public void ColorBalance_GlobalChroma_IncreasesSat()
    {
        var img = Solid(0.6f, 0.4f, 0.4f);
        float satBefore = 0.6f - 0.4f;
        new ColorBalanceRgbOp { GlobalChroma = 0.5f }.Apply(img, 1f);
        float mx = MathF.Max(img.Pixels[0], MathF.Max(img.Pixels[1], img.Pixels[2]));
        float mn = MathF.Min(img.Pixels[0], MathF.Min(img.Pixels[1], img.Pixels[2]));
        Assert.True(mx - mn > satBefore);
    }

    [Fact]
    public void ColorBalance_GainLum_BrightensHighlights()
    {
        var img = Solid(0.8f, 0.8f, 0.8f); // sáng
        float before = img.Pixels[0];
        new ColorBalanceRgbOp { GainLum = 1f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] > before);
    }

    [Fact]
    public void ColorBalance_RoundTrip()
    {
        var op = new ColorBalanceRgbOp { LiftHue = 220, LiftSat = 0.3f, GainHue = 40, GainSat = 0.25f, GlobalContrast = 0.2f };
        var back = ColorBalanceRgbOp.FromParams(op.ToParams());
        Assert.Equal(220f, back.LiftHue, 2);
        Assert.Equal(0.25f, back.GainSat, 4);
        Assert.Equal(0.2f, back.GlobalContrast, 4);
    }

    // ---- ColorContrast Lab (D2.4) ----

    [Fact]
    public void ColorContrast_Identity()
    {
        Assert.True(new ColorContrastOp().IsIdentity);
    }

    [Fact]
    public void ColorContrast_Roundtrip_GrayStays()
    {
        // xám có a*=b*=0 -> nhân hệ số không đổi gì.
        var img = Solid(0.5f, 0.5f, 0.5f);
        new ColorContrastOp { GreenMagenta = 0.5f, BlueYellow = 0.5f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.48f, 0.52f);
        Assert.InRange(img.Pixels[1], 0.48f, 0.52f);
    }

    [Fact]
    public void ColorContrast_BoostsColorfulness()
    {
        // pixel có màu -> tăng trục a/b làm nó "căng" hơn (xa xám hơn).
        var img = Solid(0.6f, 0.35f, 0.45f);
        float satBefore = MathF.Max(img.Pixels[0], MathF.Max(img.Pixels[1], img.Pixels[2]))
                        - MathF.Min(img.Pixels[0], MathF.Min(img.Pixels[1], img.Pixels[2]));
        new ColorContrastOp { GreenMagenta = 0.8f, BlueYellow = 0.8f }.Apply(img, 1f);
        float satAfter = MathF.Max(img.Pixels[0], MathF.Max(img.Pixels[1], img.Pixels[2]))
                       - MathF.Min(img.Pixels[0], MathF.Min(img.Pixels[1], img.Pixels[2]));
        Assert.True(satAfter > satBefore);
    }

    [Fact]
    public void AllD2_Registered()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(RgbLevelsOp.Type));
        Assert.True(reg.Has(VelviaOp.Type));
        Assert.True(reg.Has(ColorBalanceRgbOp.Type));
        Assert.True(reg.Has(ColorContrastOp.Type));
    }
}
