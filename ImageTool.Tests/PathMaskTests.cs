using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class PathMaskTests
{
    // đa giác vuông giữa ảnh, mỗi node feather riêng.
    private static PathMask Square(List<float>? feathers = null, bool invert = false) => new()
    {
        Points = new List<(float, float)> { (0.25f, 0.25f), (0.75f, 0.25f), (0.75f, 0.75f), (0.25f, 0.75f) },
        Feathers = feathers ?? new List<float> { 0f, 0f, 0f, 0f },
        Invert = invert,
    };

    [Fact]
    public void FewerThan3Points_EmptyMask()
    {
        var m = new PathMask { Points = new List<(float, float)> { (0.1f, 0.1f), (0.9f, 0.9f) } }.Generate(16, 16);
        foreach (var v in m) Assert.Equal(0f, v, 5);
    }

    [Fact]
    public void InsideIsOne_OutsideIsZero()
    {
        var m = Square().Generate(64, 64);
        Assert.True(m[32 * 64 + 32] > 0.99f);   // tâm trong
        Assert.True(m[2 * 64 + 2] < 0.01f);      // góc ngoài
    }

    [Fact]
    public void Invert_FlipsSelection()
    {
        var m = Square(invert: true).Generate(64, 64);
        Assert.True(m[32 * 64 + 32] < 0.01f);
        Assert.True(m[2 * 64 + 2] > 0.99f);
    }

    [Fact]
    public void PerNodeFeather_DiffersAlongEdges()
    {
        // Node trái-trên + trái-dưới feather lớn (mép trái mềm); node phải feather 0 (mép phải cứng).
        // Points: 0=(0.25,0.25) TL, 1=(0.75,0.25) TR, 2=(0.75,0.75) BR, 3=(0.25,0.75) BL.
        var m = Square(feathers: new List<float> { 0.15f, 0f, 0f, 0.15f }).Generate(128, 128);
        int y = 64;
        // Sát mép TRÁI bên trong (px ~ 0.25*127 ≈ 32, lấy cột 34): mềm -> < 1.
        float leftEdge = m[y * 128 + 35];
        // Sát mép PHẢI bên trong (px ~ 0.75*127 ≈ 95, lấy cột 93): cứng -> ~1.
        float rightEdge = m[y * 128 + 93];
        Assert.True(leftEdge < 0.99f);     // mép trái đang feather
        Assert.True(rightEdge > 0.99f);    // mép phải cứng (feather node = 0)
    }

    [Fact]
    public void RoundTrip()
    {
        var op = Square(feathers: new List<float> { 0.1f, 0.05f, 0.2f, 0.0f }, invert: true);
        var back = PathMask.FromParams(op.ToParams());
        Assert.Equal(4, back.Points.Count);
        Assert.Equal(4, back.Feathers.Count);
        Assert.Equal(0.25f, back.Points[0].X, 4);
        Assert.Equal(0.2f, back.Feathers[2], 4);
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
                    ["mask"] = PathMask.Type,
                    ["pts"] = "0.25,0.25;0.75,0.25;0.75,0.75;0.25,0.75",
                    ["feathers"] = "0;0;0;0", ["invert"] = "false",
                }
            }
        };
        var result = pipeline.Render(img, ops);
        int center = (16 * 32 + 16) * 4;
        int corner = (1 * 32 + 1) * 4;
        Assert.True(result.Pixels[center] > v * 1.5f);
        Assert.Equal(v, result.Pixels[corner], 3);
    }

    [Fact]
    public void DispatchesViaMaskedOp()
    {
        var p = new Dictionary<string, string>
        {
            ["mask"] = PathMask.Type, ["pts"] = "0,0;1,0;1,1", ["feathers"] = "0;0;0", ["inner"] = "Noop"
        };
        var op = MaskedOp.FromParams(p, EditOpRegistry.CreateDefault());
        Assert.NotNull(op);
    }
}
