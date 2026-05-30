using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class FilmNegativeOpTests
{
    private static LinearImage Solid(float r, float g, float b, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Identity_WhenDisabled()
    {
        var op = new FilmNegativeOp { Enabled = false };
        Assert.True(op.IsIdentity);
        var img = Solid(0.3f, 0.3f, 0.3f);
        float before = img.Pixels[0];
        op.Apply(img, 1f);
        Assert.Equal(before, img.Pixels[0], 5);
    }

    [Fact]
    public void Inverts_DarkNegativeBecomesBright()
    {
        // Trên negative, vùng TỐI (gần base) = vùng SÁNG trên dương bản.
        // Pixel tối hơn base -> ratio<1 -> 1/ratio>1 -> dương bản sáng.
        var op = new FilmNegativeOp { Enabled = true, RBase = 0.5f, GBase = 0.5f, BBase = 0.5f, Gamma = 1f, Exposure = 1f };
        var dark = Solid(0.1f, 0.1f, 0.1f);   // tối trên negative
        var bright = Solid(0.4f, 0.4f, 0.4f); // sáng hơn trên negative
        op.Apply(dark, 1f);
        op.Apply(bright, 1f);
        // dương bản: vùng từng tối trên negative giờ phải sáng hơn vùng từng sáng.
        Assert.True(dark.Pixels[0] > bright.Pixels[0]);
    }

    [Fact]
    public void RemovesOrangeBase_NeutralWhenChannelsProportional()
    {
        // Nếu pixel = base (×k) ở mọi kênh, sau khử base + đảo, 3 kênh phải bằng nhau (trung tính).
        var op = new FilmNegativeOp { Enabled = true, RBase = 0.5f, GBase = 0.3f, BBase = 0.18f, Gamma = 1f, Exposure = 1f };
        // pixel = 0.5×base mỗi kênh.
        var img = Solid(0.5f * 0.5f, 0.5f * 0.3f, 0.5f * 0.18f);
        op.Apply(img, 1f);
        Assert.Equal(img.Pixels[0], img.Pixels[1], 3);
        Assert.Equal(img.Pixels[1], img.Pixels[2], 3);
    }

    [Fact]
    public void Exposure_ScalesBrightness()
    {
        var baseOp = new FilmNegativeOp { Enabled = true, RBase = 0.5f, GBase = 0.5f, BBase = 0.5f, Gamma = 1f, Exposure = 1f };
        var brightOp = new FilmNegativeOp { Enabled = true, RBase = 0.5f, GBase = 0.5f, BBase = 0.5f, Gamma = 1f, Exposure = 2f };
        var a = Solid(0.2f, 0.2f, 0.2f);
        var b = Solid(0.2f, 0.2f, 0.2f);
        baseOp.Apply(a, 1f);
        brightOp.Apply(b, 1f);
        Assert.True(b.Pixels[0] > a.Pixels[0]);
    }

    [Fact]
    public void RoundTrip_Params()
    {
        var op = new FilmNegativeOp { Enabled = true, RBase = 0.55f, GBase = 0.32f, BBase = 0.2f, Gamma = 1.3f, Exposure = 1.5f };
        var back = FilmNegativeOp.FromParams(op.ToParams());
        Assert.True(back.Enabled);
        Assert.Equal(0.55f, back.RBase, 4);
        Assert.Equal(0.2f, back.BBase, 4);
        Assert.Equal(1.3f, back.Gamma, 4);
        Assert.Equal(1.5f, back.Exposure, 4);
    }

    [Fact]
    public void RegisteredInRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(FilmNegativeOp.Type));
    }

    [Fact]
    public void SampleBase_AveragesRegion()
    {
        var img = Solid(0.5f, 0.3f, 0.18f, 16, 16);
        var (r, g, b) = FilmNegativeOp.SampleBase(img, 0.5f, 0.5f, 2);
        Assert.Equal(0.5f, r, 3);
        Assert.Equal(0.3f, g, 3);
        Assert.Equal(0.18f, b, 3);
    }

    [Fact]
    public void ViaPipeline_AppliesNegative()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var baseImg = Solid(0.1f, 0.1f, 0.1f);
        var ops = new List<EditOperation>
        {
            new EditOperation
            {
                OpType = FilmNegativeOp.Type,
                Params = new() { ["enabled"] = "true", ["rbase"] = "0.5", ["gbase"] = "0.5", ["bbase"] = "0.5", ["gamma"] = "1", ["exposure"] = "1" }
            }
        };
        var result = pipeline.Render(baseImg, ops);
        // vùng tối trên negative -> sáng trên dương bản (>0.1 gốc).
        Assert.True(result.Pixels[0] > 0.1f);
    }
}
