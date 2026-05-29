using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class NewOpsTests
{
    private static LinearImage Solid(float r, float g, float b, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void SplitToning_Identity_NoChange()
    {
        var op = new SplitToningOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.5f, 0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void SplitToning_RoundTrip()
    {
        var op = new SplitToningOp { HiHue = 50f, HiSat = 0.4f, ShHue = 220f, ShSat = 0.3f, Balance = 0.2f };
        var back = SplitToningOp.FromParams(op.ToParams());
        Assert.Equal(50f, back.HiHue, 2);
        Assert.Equal(0.3f, back.ShSat, 4);
    }

    [Fact]
    public void ChannelMixer_Identity_NoChange()
    {
        var op = new ChannelMixerOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.6f, 0.3f, 0.2f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.599f, 0.601f);
    }

    [Fact]
    public void ColorNR_Identity_NoChange()
    {
        var op = new ColorNoiseReductionOp { Amount = 0f };
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.4f, 0.3f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void ColorNR_PreservesLuminanceRoughly()
    {
        // ảnh nhiễu màu nhẹ; sau NR luminance trung bình không đổi nhiều.
        var img = new LinearImage(8, 8);
        var rnd = new Random(1);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = 0.5f + (float)(rnd.NextDouble() - 0.5) * 0.2f;
            img.Pixels[i + 1] = 0.5f;
            img.Pixels[i + 2] = 0.5f + (float)(rnd.NextDouble() - 0.5) * 0.2f;
            img.Pixels[i + 3] = 1f;
        }
        float lumBefore = 0; for (int i = 0; i < img.Pixels.Length; i += 4) lumBefore += ColorSpace.Luminance(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2]);
        new ColorNoiseReductionOp { Amount = 1f }.Apply(img, 1f);
        float lumAfter = 0; for (int i = 0; i < img.Pixels.Length; i += 4) lumAfter += ColorSpace.Luminance(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2]);
        Assert.InRange(lumAfter, lumBefore - 0.5f, lumBefore + 0.5f);
    }

    [Fact]
    public void LumaNR_Identity_NoChange()
    {
        var op = new LumaNoiseReductionOp { Amount = 0f };
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.5f, 0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void Defringe_Identity_NoChange()
    {
        var op = new DefringeOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.2f, 0.6f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void Defringe_ReducesPurpleSaturation()
    {
        // pixel tím rực: R cao, B cao, G thấp.
        var img = Solid(0.6f, 0.1f, 0.7f);
        float satBefore = (0.7f - 0.1f);
        new DefringeOp { Purple = 1f }.Apply(img, 1f);
        float satAfter = MathF.Max(img.Pixels[0], img.Pixels[2]) - img.Pixels[1];
        Assert.True(satAfter < satBefore);
    }

    [Fact]
    public void Dehaze_Identity_NoChange()
    {
        var op = new DehazeOp { Amount = 0f };
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.5f, 0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void Filmic_CompressesHighlights()
    {
        var img = Solid(3.0f, 3.0f, 3.0f); // highlight vượt 1.0
        new FilmicOp { Amount = 1f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] <= 1.01f); // nén về <=1
    }

    [Fact]
    public void Filmic_Identity_NoChange()
    {
        var op = new FilmicOp { Amount = 0f };
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.5f, 0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void ParametricCurve_Identity_NoChange()
    {
        var op = new ParametricCurveOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.5f, 0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void ParametricCurve_ShadowsLiftBrightensDark()
    {
        var img = Solid(ColorSpace.SrgbToLinear(0.1f), ColorSpace.SrgbToLinear(0.1f), ColorSpace.SrgbToLinear(0.1f));
        float before = img.Pixels[0];
        new ParametricCurveOp { Shadows = 1f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] > before);
    }

    [Fact]
    public void AllNewOps_RegisteredInRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        foreach (var t in new[] { SplitToningOp.Type, ChannelMixerOp.Type, ColorNoiseReductionOp.Type,
            LumaNoiseReductionOp.Type, DefringeOp.Type, DehazeOp.Type, FilmicOp.Type, ParametricCurveOp.Type })
            Assert.True(reg.Has(t), $"Op {t} chưa đăng ký");
    }
}
