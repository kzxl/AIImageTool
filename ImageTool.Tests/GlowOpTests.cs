using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class GlowOpTests
{
    private static LinearImage Solid(float v, int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Identity_WhenZeroAmount()
    {
        Assert.True(new GlowOp { Amount = 0 }.IsIdentity);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(GlowOp.Type));
    }

    [Fact]
    public void RoundTrip()
    {
        var back = GlowOp.FromParams(new GlowOp { Amount = 0.6f, BaseRadius = 20f, Threshold = 0.3f }.ToParams());
        Assert.Equal(0.6f, back.Amount, 4);
        Assert.Equal(20f, back.BaseRadius, 4);
        Assert.Equal(0.3f, back.Threshold, 4);
    }

    [Fact]
    public void Brightens_ViaScreenBlend()
    {
        // screen với chính bản mờ luôn làm sáng hơn (trừ 0 và 1).
        var img = Solid(0.4f);
        float before = img.Pixels[0];
        new GlowOp { Amount = 1f, BaseRadius = 6f, Threshold = 0f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] > before);
    }

    [Fact]
    public void Black_StaysBlack()
    {
        var img = Solid(0f);
        new GlowOp { Amount = 1f, BaseRadius = 6f }.Apply(img, 1f);
        Assert.Equal(0f, img.Pixels[0], 5);
    }

    [Fact]
    public void Threshold_SkipsDarkAreas()
    {
        // ảnh tối dưới ngưỡng -> bright-pass ~0 -> glow gần như không tác động.
        var img = Solid(0.1f);
        float before = img.Pixels[0];
        new GlowOp { Amount = 1f, BaseRadius = 6f, Threshold = 0.6f }.Apply(img, 1f);
        Assert.Equal(before, img.Pixels[0], 2);
    }

    [Fact]
    public void PreservesAlpha()
    {
        var img = Solid(0.5f);
        new GlowOp { Amount = 1f, BaseRadius = 4f }.Apply(img, 1f);
        for (int i = 3; i < img.Pixels.Length; i += 4)
            Assert.Equal(1f, img.Pixels[i], 5);
    }
}
