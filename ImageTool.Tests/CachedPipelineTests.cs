using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class CachedPipelineTests
{
    private static LinearImage Gradient(int w = 24, int h = 24)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                img.Pixels[o] = ColorSpace.SrgbToLinear(x / (float)(w - 1));
                img.Pixels[o + 1] = ColorSpace.SrgbToLinear(y / (float)(h - 1));
                img.Pixels[o + 2] = 0.3f;
                img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    private static EditOperation Exp(float ev) => new()
    {
        PluginId = "Develop", OpType = DevelopBasicOp.Type,
        Params = new() { ["exposure"] = ev.ToString(System.Globalization.CultureInfo.InvariantCulture) }
    };

    private static EditOperation Contrast(float c) => new()
    {
        PluginId = "Develop", OpType = "DevelopBasic2",
        Params = new() { ["contrast"] = c.ToString(System.Globalization.CultureInfo.InvariantCulture) }
    };

    private static void AssertSame(LinearImage a, LinearImage b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        for (int i = 0; i < a.Pixels.Length; i++)
            Assert.InRange(b.Pixels[i], a.Pixels[i] - 1e-4f, a.Pixels[i] + 1e-4f);
    }

    [Fact]
    public void Cached_MatchesPlain_SingleRender()
    {
        var reg = EditOpRegistry.CreateDefault();
        var plain = new EditPipeline(reg);
        var cached = new CachedEditPipeline(reg);
        var ops = new List<EditOperation>
        {
            Exp(1f),
            new EditOperation { PluginId = "Develop", OpType = ClarityOp.Type, Params = new() { ["amount"] = "0.5" } },
        };
        var baseImg = Gradient();
        var a = plain.RenderScaled(baseImg, ops, 1f);
        var b = cached.RenderScaled("img1", baseImg, ops, 1f);
        AssertSame(a, b);
    }

    [Fact]
    public void Cached_MatchesPlain_AfterEditingLastOp()
    {
        var reg = EditOpRegistry.CreateDefault();
        var plain = new EditPipeline(reg);
        var cached = new CachedEditPipeline(reg);
        var baseImg = Gradient();

        var ops = new List<EditOperation>
        {
            new EditOperation { PluginId = "Develop", OpType = HslMixerOp.Type, Params = new() { ["s_red"] = "0.4" } },
            new EditOperation { PluginId = "Develop", OpType = VignetteOp.Type, Params = new() { ["amount"] = "-0.5" } },
        };
        // render lần 1 (xây cache).
        cached.RenderScaled("img1", baseImg, ops, 1f);

        // đổi op cuối -> cache nên replay từ op cuối.
        ops[1] = new EditOperation { PluginId = "Develop", OpType = VignetteOp.Type, Params = new() { ["amount"] = "-0.8" } };
        var a = plain.RenderScaled(baseImg, ops, 1f);
        var b = cached.RenderScaled("img1", baseImg, ops, 1f);
        AssertSame(a, b);
    }

    [Fact]
    public void Cached_MatchesPlain_AfterEditingFirstOp()
    {
        var reg = EditOpRegistry.CreateDefault();
        var plain = new EditPipeline(reg);
        var cached = new CachedEditPipeline(reg);
        var baseImg = Gradient();

        var ops = new List<EditOperation> { Exp(1f), new EditOperation { PluginId = "Develop", OpType = SharpenOp.Type, Params = new() { ["amount"] = "0.5" } } };
        cached.RenderScaled("img1", baseImg, ops, 1f);

        ops[0] = Exp(-1f); // đổi op đầu -> phải replay toàn bộ.
        var a = plain.RenderScaled(baseImg, ops, 1f);
        var b = cached.RenderScaled("img1", baseImg, ops, 1f);
        AssertSame(a, b);
    }

    [Fact]
    public void Cached_MatchesPlain_WhenOpsAppended()
    {
        var reg = EditOpRegistry.CreateDefault();
        var plain = new EditPipeline(reg);
        var cached = new CachedEditPipeline(reg);
        var baseImg = Gradient();

        var ops = new List<EditOperation> { Exp(0.5f) };
        cached.RenderScaled("img1", baseImg, ops, 1f);

        ops.Add(new EditOperation { PluginId = "Develop", OpType = GrainOp.Type, Params = new() { ["amount"] = "0.3" } });
        var a = plain.RenderScaled(baseImg, ops, 1f);
        var b = cached.RenderScaled("img1", baseImg, ops, 1f);
        AssertSame(a, b);
    }

    [Fact]
    public void Cached_MatchesPlain_WithResizingOp()
    {
        var reg = EditOpRegistry.CreateDefault();
        var plain = new EditPipeline(reg);
        var cached = new CachedEditPipeline(reg);
        var baseImg = Gradient(20, 16);

        var ops = new List<EditOperation>
        {
            new EditOperation { PluginId = "Develop", OpType = CropOp.Type, Params = new() { ["x"] = "0.1", ["y"] = "0.1", ["w"] = "0.8", ["h"] = "0.8" } },
            Exp(0.7f),
        };
        cached.RenderScaled("img1", baseImg, ops, 1f);
        ops[1] = Exp(1.2f);
        var a = plain.RenderScaled(baseImg, ops, 1f);
        var b = cached.RenderScaled("img1", baseImg, ops, 1f);
        AssertSame(a, b);
    }

    [Fact]
    public void Cached_InvalidatesOnKeyChange()
    {
        var reg = EditOpRegistry.CreateDefault();
        var plain = new EditPipeline(reg);
        var cached = new CachedEditPipeline(reg);
        var img1 = Gradient();
        var img2 = Gradient();
        // làm img2 khác img1.
        img2.Pixels[0] = 0.9f;

        var ops = new List<EditOperation> { Exp(1f) };
        cached.RenderScaled("img1", img1, ops, 1f);
        var b = cached.RenderScaled("img2", img2, ops, 1f); // key đổi -> rebuild từ img2
        var a = plain.RenderScaled(img2, ops, 1f);
        AssertSame(a, b);
    }

    [Fact]
    public void Cached_RespectsCount()
    {
        var reg = EditOpRegistry.CreateDefault();
        var plain = new EditPipeline(reg);
        var cached = new CachedEditPipeline(reg);
        var baseImg = Gradient();
        var ops = new List<EditOperation> { Exp(1f), Exp(1f), Exp(1f) };
        // pointer = 2 -> chỉ áp 2 op đầu.
        var a = plain.RenderScaled(baseImg, ops, 1f, 2);
        var b = cached.RenderScaled("img1", baseImg, ops, 1f, 2);
        AssertSame(a, b);
    }

    [Fact]
    public void Cached_DepthBounded()
    {
        var reg = EditOpRegistry.CreateDefault();
        var cached = new CachedEditPipeline(reg) { MaxCheckpoints = 3 };
        var baseImg = Gradient();
        var ops = new List<EditOperation>();
        for (int i = 0; i < 10; i++) ops.Add(Exp(0.1f));
        cached.RenderScaled("img1", baseImg, ops, 1f);
        Assert.True(cached.CachedDepth <= 3);
    }
}
