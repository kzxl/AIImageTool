using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class PolygonMaskTests
{
    // đa giác vuông chiếm 1/4 giữa ảnh: (0.25,0.25)-(0.75,0.25)-(0.75,0.75)-(0.25,0.75).
    private static PolygonMask Square(float feather = 0f, bool invert = false) => new()
    {
        Points = new List<(float, float)> { (0.25f, 0.25f), (0.75f, 0.25f), (0.75f, 0.75f), (0.25f, 0.75f) },
        Feather = feather,
        Invert = invert,
    };

    [Fact]
    public void FewerThan3Points_EmptyMask()
    {
        var m = new PolygonMask { Points = new List<(float, float)> { (0.1f, 0.1f), (0.9f, 0.9f) } }.Generate(16, 16);
        foreach (var v in m) Assert.Equal(0f, v, 5);
    }

    [Fact]
    public void InsideIsOne_OutsideIsZero()
    {
        var m = Square().Generate(64, 64);
        // tâm (32,32) trong đa giác.
        Assert.True(m[32 * 64 + 32] > 0.99f);
        // góc (2,2) ngoài.
        Assert.True(m[2 * 64 + 2] < 0.01f);
    }

    [Fact]
    public void Invert_FlipsSelection()
    {
        var m = Square(invert: true).Generate(64, 64);
        Assert.True(m[32 * 64 + 32] < 0.01f); // tâm giờ = 0
        Assert.True(m[2 * 64 + 2] > 0.99f);    // góc = 1
    }

    [Fact]
    public void Feather_SoftensEdge()
    {
        var m = Square(feather: 0.1f).Generate(64, 64);
        // điểm ngay sát biên trong đa giác (gần x=0.25 -> px~16) phải < 1 (đang feather).
        int y = 32;
        // x ngay trong mép trái: cột 17
        float edge = m[y * 64 + 17];
        float core = m[y * 64 + 32];
        Assert.True(core > edge); // lõi đậm hơn mép
        Assert.True(edge < 1f);
    }

    [Fact]
    public void RoundTrip()
    {
        var op = Square(feather: 0.08f, invert: true);
        var back = PolygonMask.FromParams(op.ToParams());
        Assert.Equal(4, back.Points.Count);
        Assert.Equal(0.25f, back.Points[0].X, 4);
        Assert.Equal(0.08f, back.Feather, 4);
        Assert.True(back.Invert);
    }

    [Fact]
    public void ReplaysViaPipeline()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var img = new LinearImage(32, 32);
        float v = ColorSpace.SrgbToLinear(0.3f);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = img.Pixels[i + 1] = img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }

        var ops = new List<EditOperation>
        {
            new EditOperation
            {
                OpType = MaskedOp.Type,
                Params = new Dictionary<string, string>
                {
                    ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
                    ["mask"] = PolygonMask.Type,
                    ["pts"] = "0.25,0.25;0.75,0.25;0.75,0.75;0.25,0.75",
                    ["feather"] = "0", ["invert"] = "false",
                }
            }
        };
        var result = pipeline.Render(img, ops);
        // tâm (trong đa giác) sáng lên; góc (ngoài) gần như không đổi.
        int center = (16 * 32 + 16) * 4;
        int corner = (1 * 32 + 1) * 4;
        Assert.True(result.Pixels[center] > v * 1.5f);
        Assert.Equal(v, result.Pixels[corner], 3);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(MaskedOp.Type));
        // PolygonMask không tự đăng ký op (qua MaskedOp); kiểm tra dispatch FromParams hoạt động.
        var p = new Dictionary<string, string> { ["mask"] = PolygonMask.Type, ["pts"] = "0,0;1,0;1,1", ["inner"] = "Noop" };
        var op = MaskedOp.FromParams(p, EditOpRegistry.CreateDefault());
        Assert.NotNull(op);
    }
}
