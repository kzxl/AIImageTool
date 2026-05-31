using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ColorMatchTests
{
    private static LinearImage Solid(float r, float g, float b, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = ColorSpace.SrgbToLinear(r);
            img.Pixels[i + 1] = ColorSpace.SrgbToLinear(g);
            img.Pixels[i + 2] = ColorSpace.SrgbToLinear(b);
            img.Pixels[i + 3] = 1f;
        }
        return img;
    }

    // Anh gradient de co std > 0 (Reinhard can phuong sai).
    private static LinearImage Gradient(float baseR, float baseG, float baseB, int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float t = (x + y) / (float)(w + h);
                p[o] = ColorSpace.SrgbToLinear(System.Math.Clamp(baseR + t * 0.3f, 0f, 1f));
                p[o + 1] = ColorSpace.SrgbToLinear(System.Math.Clamp(baseG + t * 0.2f, 0f, 1f));
                p[o + 2] = ColorSpace.SrgbToLinear(System.Math.Clamp(baseB + t * 0.1f, 0f, 1f));
                p[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void ZeroStrength_NoChange()
    {
        var tgt = Gradient(0.3f, 0.3f, 0.3f);
        var orig = tgt.Clone();
        var refImg = Gradient(0.6f, 0.2f, 0.1f);
        ColorMatch.Apply(tgt, refImg, 0f);
        for (int i = 0; i < tgt.Pixels.Length; i++)
            Assert.Equal(orig.Pixels[i], tgt.Pixels[i], 4);
    }

    [Fact]
    public void Match_ShiftsMeanTowardReference()
    {
        // Đích xám trung tính, tham chiếu ám cam -> sau match, đích phải ấm hơn (R > B).
        var tgt = Gradient(0.4f, 0.4f, 0.4f);
        var refImg = Gradient(0.7f, 0.4f, 0.15f);
        ColorMatch.Apply(tgt, refImg, 1f);

        double sr = 0, sb = 0; int n = tgt.PixelCount;
        for (int i = 0; i < n; i++)
        {
            sr += ColorSpace.LinearToSrgb(tgt.Pixels[i * 4]);
            sb += ColorSpace.LinearToSrgb(tgt.Pixels[i * 4 + 2]);
        }
        Assert.True(sr / n > sb / n, "sau color match đích phải ấm hơn (R>B)");
    }

    [Fact]
    public void FullStrength_MeanApproximatesReference()
    {
        var tgt = Gradient(0.2f, 0.5f, 0.7f);
        var refImg = Gradient(0.6f, 0.3f, 0.2f);
        var refMeanR = MeanSrgb(refImg, 0);
        ColorMatch.Apply(tgt, refImg, 1f);
        var tgtMeanR = MeanSrgb(tgt, 0);
        // Mean kênh R của đích sau match xấp xỉ mean của tham chiếu (sai số do clamp/gamut).
        Assert.True(System.Math.Abs(tgtMeanR - refMeanR) < 0.12, $"tgt={tgtMeanR:F3} ref={refMeanR:F3}");
    }

    [Fact]
    public void FlatTarget_DoesNotThrow_AndStaysFinite()
    {
        var tgt = Solid(0.5f, 0.5f, 0.5f); // std ~ 0
        var refImg = Gradient(0.6f, 0.2f, 0.1f);
        ColorMatch.Apply(tgt, refImg, 1f);
        foreach (var v in tgt.Pixels) Assert.True(float.IsFinite(v));
    }

    [Fact]
    public void NullReference_NoThrow()
    {
        var tgt = Gradient(0.3f, 0.3f, 0.3f);
        ColorMatch.Apply(tgt, null!, 1f); // không ném
    }

    private static double MeanSrgb(LinearImage img, int channel)
    {
        double s = 0; int n = img.PixelCount;
        for (int i = 0; i < n; i++) s += ColorSpace.LinearToSrgb(img.Pixels[i * 4 + channel]);
        return s / n;
    }

    // === ColorMatchOp (replay qua stats) ===

    [Fact]
    public void Op_RoundTrip_Params()
    {
        var refImg = Gradient(0.6f, 0.3f, 0.2f);
        var stats = ColorMatch.Measure(refImg);
        var op = new ColorMatchOp
        {
            ML = stats.ML, Ma = stats.Ma, Mb = stats.Mb,
            SL = stats.SL, Sa = stats.Sa, Sb = stats.Sb,
            Strength = 0.8f
        };
        var back = ColorMatchOp.FromParams(op.ToParams());
        Assert.Equal(op.ML, back.ML, 2);
        Assert.Equal(op.Sa, back.Sa, 2);
        Assert.Equal(0.8f, back.Strength, 2);
    }

    [Fact]
    public void Op_Disabled_WhenZeroStrength()
    {
        var op = new ColorMatchOp { SL = 10f, Strength = 0f };
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void Op_Disabled_WhenUnconfigured()
    {
        var op = new ColorMatchOp { Strength = 1f }; // SL = -1 default
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void Op_Apply_MatchesDirectApply()
    {
        var refImg = Gradient(0.7f, 0.3f, 0.15f);
        var stats = ColorMatch.Measure(refImg);

        var a = Gradient(0.3f, 0.5f, 0.6f);
        var b = a.Clone();

        ColorMatch.ApplyStats(a, stats, 1f);
        new ColorMatchOp
        {
            ML = stats.ML, Ma = stats.Ma, Mb = stats.Mb,
            SL = stats.SL, Sa = stats.Sa, Sb = stats.Sb, Strength = 1f
        }.Apply(b, 1f);

        for (int i = 0; i < a.Pixels.Length; i++)
            Assert.Equal(a.Pixels[i], b.Pixels[i], 4);
    }

    [Fact]
    public void Op_Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(ColorMatchOp.Type));
    }
}
