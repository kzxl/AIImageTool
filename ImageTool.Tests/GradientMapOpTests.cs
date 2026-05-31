using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class GradientMapOpTests
{
    private static LinearImage Gray(float srgb, int w = 4, int h = 4)
    {
        var img = new LinearImage(w, h);
        float lin = ColorSpace.SrgbToLinear(srgb);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = lin; img.Pixels[i + 1] = lin; img.Pixels[i + 2] = lin; img.Pixels[i + 3] = 1f;
        }
        return img;
    }

    [Fact]
    public void ZeroOpacity_IsIdentity()
    {
        var op = new GradientMapOp { Opacity = 0f };
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void BlackToWhite_MapsDarkToShadowColor()
    {
        // Gradient sh=đỏ, hi=lục; opacity=1; ảnh tối -> ra đỏ.
        var img = Gray(0.05f);
        var op = new GradientMapOp
        {
            ShadowR = 1f, ShadowG = 0f, ShadowB = 0f,
            MidR = 1f, MidG = 0f, MidB = 0f,
            HighR = 0f, HighG = 1f, HighB = 0f,
            Opacity = 1f, MidPoint = 0.5f
        };
        op.Apply(img, 1f);
        float r = ColorSpace.LinearToSrgb(img.Pixels[0]);
        float g = ColorSpace.LinearToSrgb(img.Pixels[1]);
        Assert.True(r > 0.8f, $"R={r} nên đỏ");
        Assert.True(g < 0.2f, $"G={g} nên thấp");
    }

    [Fact]
    public void Bright_MapsToHighlightColor()
    {
        var img = Gray(0.95f);
        var op = new GradientMapOp
        {
            ShadowR = 1f, ShadowG = 0f, ShadowB = 0f,
            MidR = 1f, MidG = 0f, MidB = 0f,
            HighR = 0f, HighG = 1f, HighB = 0f,
            Opacity = 1f
        };
        op.Apply(img, 1f);
        float g = ColorSpace.LinearToSrgb(img.Pixels[1]);
        Assert.True(g > 0.8f, $"G={g} nên lục (highlight)");
    }

    [Fact]
    public void Opacity_BlendsPartially()
    {
        var orig = Gray(0.5f);
        var img = orig.Clone();
        var op = new GradientMapOp
        {
            ShadowR = 0f, ShadowG = 0f, ShadowB = 1f,
            MidR = 0f, MidG = 0f, MidB = 1f,
            HighR = 0f, HighG = 0f, HighB = 1f,
            Opacity = 0.5f
        };
        op.Apply(img, 1f);
        // B kênh phải tăng (về phía xanh) nhưng chưa hoàn toàn.
        Assert.True(img.Pixels[2] > orig.Pixels[2]);
    }

    [Fact]
    public void RoundTrip_Params()
    {
        var op = new GradientMapOp
        {
            ShadowR = 0.1f, ShadowG = 0.2f, ShadowB = 0.3f,
            MidR = 0.4f, MidG = 0.5f, MidB = 0.6f,
            HighR = 0.7f, HighG = 0.8f, HighB = 0.9f,
            MidPoint = 0.4f, Opacity = 0.75f
        };
        var back = GradientMapOp.FromParams(op.ToParams());
        Assert.Equal(0.4f, back.MidPoint, 2);
        Assert.Equal(0.75f, back.Opacity, 2);
        // Màu round-trip qua hex (sai số <=1/255).
        Assert.Equal(op.ShadowB, back.ShadowB, 2);
        Assert.Equal(op.HighG, back.HighG, 2);
    }

    [Fact]
    public void Registered_InDefaultRegistry()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(GradientMapOp.Type));
    }
}
