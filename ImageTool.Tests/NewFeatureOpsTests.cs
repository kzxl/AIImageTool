using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class NewFeatureOpsTests
{
    private static LinearImage Solid(float r, float g, float b, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // ---- BlackWhiteOp (13.1) ----

    [Fact]
    public void BlackWhite_Disabled_IsIdentity()
    {
        var op = new BlackWhiteOp { Enabled = false };
        Assert.True(op.IsIdentity);
        var img = Solid(0.6f, 0.3f, 0.1f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.599f, 0.601f);
    }

    [Fact]
    public void BlackWhite_ProducesGray()
    {
        var op = new BlackWhiteOp { Enabled = true };
        var img = Solid(0.6f, 0.3f, 0.1f);
        op.Apply(img, 1f);
        // R=G=B sau khi chuyển xám.
        Assert.Equal(img.Pixels[0], img.Pixels[1], 4);
        Assert.Equal(img.Pixels[1], img.Pixels[2], 4);
    }

    [Fact]
    public void BlackWhite_ChannelWeights_AffectGray()
    {
        var imgRed = Solid(0.8f, 0.1f, 0.1f);
        var imgRedHeavy = Solid(0.8f, 0.1f, 0.1f);
        new BlackWhiteOp { Enabled = true, RedWeight = 0.1f, GreenWeight = 0.8f, BlueWeight = 0.1f }.Apply(imgRed, 1f);
        new BlackWhiteOp { Enabled = true, RedWeight = 0.8f, GreenWeight = 0.1f, BlueWeight = 0.1f }.Apply(imgRedHeavy, 1f);
        // ảnh đỏ: tăng trọng số đỏ -> xám sáng hơn.
        Assert.True(imgRedHeavy.Pixels[0] > imgRed.Pixels[0]);
    }

    [Fact]
    public void BlackWhite_RoundTrip()
    {
        var op = new BlackWhiteOp { Enabled = true, RedWeight = 0.4f, ToneHue = 40f, ToneStrength = 0.3f };
        var back = BlackWhiteOp.FromParams(op.ToParams());
        Assert.True(back.Enabled);
        Assert.Equal(0.4f, back.RedWeight, 4);
        Assert.Equal(0.3f, back.ToneStrength, 4);
    }

    [Fact]
    public void BlackWhite_RedFilter_DarkensBlueSky_VsBlueFilter()
    {
        // Trời xanh: red filter (kính lọc đỏ) làm trời TỐI hơn; blue filter làm trời SÁNG hơn.
        var sky = Solid(0.2f, 0.4f, 0.8f);
        var skyRed = Solid(0.2f, 0.4f, 0.8f);
        var skyBlue = Solid(0.2f, 0.4f, 0.8f);
        // weights như preset trong UI.
        new BlackWhiteOp { Enabled = true, RedWeight = 0.80f, GreenWeight = 0.15f, BlueWeight = 0.05f }.Apply(skyRed, 1f);
        new BlackWhiteOp { Enabled = true, RedWeight = 0.05f, GreenWeight = 0.25f, BlueWeight = 0.70f }.Apply(skyBlue, 1f);
        Assert.True(skyRed.Pixels[0] < skyBlue.Pixels[0], "red filter phải làm trời xanh tối hơn blue filter");
    }

    // ---- InvertOp (13.3) ----

    [Fact]
    public void Invert_Disabled_IsIdentity()
    {
        var op = new InvertOp { Enabled = false };
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void Invert_BlackBecomesWhite()
    {
        var img = Solid(0f, 0f, 0f);
        new InvertOp { Enabled = true }.Apply(img, 1f);
        // đen -> trắng (linear ~1).
        Assert.True(img.Pixels[0] > 0.99f);
    }

    [Fact]
    public void Invert_Twice_RestoresOriginal()
    {
        var img = Solid(ColorSpace.SrgbToLinear(0.3f), ColorSpace.SrgbToLinear(0.6f), ColorSpace.SrgbToLinear(0.8f));
        float r0 = img.Pixels[0], g0 = img.Pixels[1], b0 = img.Pixels[2];
        var op = new InvertOp { Enabled = true };
        op.Apply(img, 1f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], r0 - 1e-3f, r0 + 1e-3f);
        Assert.InRange(img.Pixels[1], g0 - 1e-3f, g0 + 1e-3f);
        Assert.InRange(img.Pixels[2], b0 - 1e-3f, b0 + 1e-3f);
    }

    // ---- ChannelGainOp (13.2 apply) ----

    [Fact]
    public void ChannelGain_Identity_NoChange()
    {
        var op = new ChannelGainOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.5f, 0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void ChannelGain_ScalesChannels()
    {
        var img = Solid(0.4f, 0.4f, 0.4f);
        new ChannelGainOp { R = 1.5f, G = 1f, B = 0.5f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.599f, 0.601f);
        Assert.InRange(img.Pixels[2], 0.199f, 0.201f);
    }

    // ---- AutoWhiteBalance (13.2) ----

    [Fact]
    public void AutoWB_GrayWorld_NeutralizesCast()
    {
        // ảnh ám đỏ: R cao hơn -> gain R < 1, gain B > 1.
        var img = Solid(0.6f, 0.4f, 0.3f);
        var gains = AutoWhiteBalance.Analyze(img, AutoWhiteBalance.Strategy.GrayWorld);
        Assert.True(gains.R < 1f);
        Assert.True(gains.B > 1f);
        Assert.Equal(1f, gains.G, 4); // chuẩn hoá theo G
    }

    [Fact]
    public void AutoWB_NeutralImage_NoCorrection()
    {
        var img = Solid(0.5f, 0.5f, 0.5f);
        var gains = AutoWhiteBalance.Analyze(img);
        Assert.True(gains.IsNeutral);
    }

    [Fact]
    public void AutoWB_AppliedGains_BalanceChannels()
    {
        var img = Solid(0.6f, 0.4f, 0.3f);
        var gains = AutoWhiteBalance.Analyze(img);
        new ChannelGainOp { R = gains.R, G = gains.G, B = gains.B }.Apply(img, 1f);
        // sau cân bằng, 3 kênh xích lại gần nhau hơn.
        float spread = System.MathF.Max(img.Pixels[0], System.MathF.Max(img.Pixels[1], img.Pixels[2]))
                     - System.MathF.Min(img.Pixels[0], System.MathF.Min(img.Pixels[1], img.Pixels[2]));
        Assert.True(spread < 0.3f - 0.05f); // ban đầu spread = 0.3
    }

    [Fact]
    public void AutoWB_WhitePatch_Works()
    {
        var img = Solid(0.8f, 0.6f, 0.4f);
        var gains = AutoWhiteBalance.Analyze(img, AutoWhiteBalance.Strategy.WhitePatch);
        Assert.True(gains.B > gains.R); // bù kênh yếu nhất (B) nhiều hơn
    }

    // ---- All registered ----

    [Fact]
    public void NewOps_Registered()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(BlackWhiteOp.Type));
        Assert.True(reg.Has(InvertOp.Type));
        Assert.True(reg.Has(ChannelGainOp.Type));
    }
}

public class CropAspectTests
{
    [Fact]
    public void Square_FromLandscape_LimitedByHeight()
    {
        var r = CropAspect.Centered(200, 100, 1, 1);
        // ảnh 200x100, crop vuông -> 100x100, w=0.5, căn giữa x=0.25.
        Assert.Equal(0.5f, r.W, 3);
        Assert.Equal(1f, r.H, 3);
        Assert.Equal(0.25f, r.X, 3);
        Assert.Equal(0f, r.Y, 3);
    }

    [Fact]
    public void Square_FromPortrait_LimitedByWidth()
    {
        var r = CropAspect.Centered(100, 200, 1, 1);
        Assert.Equal(1f, r.W, 3);
        Assert.Equal(0.5f, r.H, 3);
        Assert.Equal(0f, r.X, 3);
        Assert.Equal(0.25f, r.Y, 3);
    }

    [Fact]
    public void SixteenNine_FromSquare()
    {
        var r = CropAspect.Centered(100, 100, 16, 9);
        // 16:9 trong ảnh vuông -> full width, height = 100*9/16 = 56.25px -> h ~0.5625.
        Assert.Equal(1f, r.W, 3);
        Assert.Equal(0.5625f, r.H, 3);
    }

    [Fact]
    public void MatchingAspect_FullFrame()
    {
        var r = CropAspect.Centered(160, 90, 16, 9);
        Assert.Equal(1f, r.W, 3);
        Assert.Equal(1f, r.H, 3);
    }

    [Fact]
    public void InvalidRatio_ReturnsFull()
    {
        var r = CropAspect.Centered(100, 100, 0, 0);
        Assert.Equal(1f, r.W, 3);
        Assert.Equal(1f, r.H, 3);
    }

    [Fact]
    public void Presets_ContainsCommon()
    {
        Assert.Contains(CropAspect.Presets, p => p.Name == "16:9");
        Assert.Contains(CropAspect.Presets, p => p.Name == "1:1");
    }
}
