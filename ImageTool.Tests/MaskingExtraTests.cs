using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class MaskingExtraTests
{
    private static LinearImage Solid(float r, float g, float b, int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // ---- ColorUnifyOp (3.7) ----

    [Fact]
    public void ColorUnify_Identity_NoChange()
    {
        var op = new ColorUnifyOp { Intensity = 0f };
        Assert.True(op.IsIdentity);
        var img = Solid(0.6f, 0.2f, 0.1f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.599f, 0.601f);
    }

    [Fact]
    public void ColorUnify_ShiftsHueTowardTarget()
    {
        // pixel đỏ (hue ~0). Target hue = 240 (xanh). Intensity 1 -> hue về gần xanh.
        var img = Solid(ColorSpace.SrgbToLinear(0.8f), ColorSpace.SrgbToLinear(0.1f), ColorSpace.SrgbToLinear(0.1f), 4, 4);
        new ColorUnifyOp { TargetHue = 240f, TargetSat = 0.6f, Intensity = 1f }.Apply(img, 1f);
        // sau dịch toàn phần, B phải vượt R.
        Assert.True(img.Pixels[2] > img.Pixels[0]);
    }

    [Fact]
    public void ColorUnify_GrayPixelUnchanged()
    {
        var img = Solid(0.5f, 0.5f, 0.5f, 4, 4);
        new ColorUnifyOp { TargetHue = 120f, Intensity = 1f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
        Assert.InRange(img.Pixels[2], 0.499f, 0.501f);
    }

    [Fact]
    public void ColorUnify_RoundTripThroughRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(ColorUnifyOp.Type));
        var p = new ColorUnifyOp { TargetHue = 200f, TargetSat = 0.4f, Intensity = 0.5f }.ToParams();
        var back = (ColorUnifyOp)reg.Create(ColorUnifyOp.Type, p)!;
        Assert.Equal(200f, back.TargetHue, 2);
        Assert.Equal(0.5f, back.Intensity, 4);
    }

    // ---- ColorRangeMask (6.5) ----

    [Fact]
    public void ColorRangeMask_SelectsTargetHueOnly()
    {
        // 2 pixel: đỏ (hue 0) và xanh dương (hue 240).
        var img = new LinearImage(2, 1);
        img.Pixels[0] = ColorSpace.SrgbToLinear(0.9f); img.Pixels[1] = ColorSpace.SrgbToLinear(0.1f); img.Pixels[2] = ColorSpace.SrgbToLinear(0.1f); img.Pixels[3] = 1f;
        img.Pixels[4] = ColorSpace.SrgbToLinear(0.1f); img.Pixels[5] = ColorSpace.SrgbToLinear(0.1f); img.Pixels[6] = ColorSpace.SrgbToLinear(0.9f); img.Pixels[7] = 1f;
        var m = new ColorRangeMask { TargetHue = 0f, HueRange = 30f, MinSat = 0.1f, Smooth = 0.1f }.GenerateFrom(img);
        Assert.True(m[0] > 0.9f);  // đỏ được chọn
        Assert.True(m[1] < 0.1f);  // xanh không
    }

    [Fact]
    public void ColorRangeMask_LowSatExcluded()
    {
        var img = Solid(0.5f, 0.5f, 0.5f, 2, 2); // xám
        var m = new ColorRangeMask { TargetHue = 0f, HueRange = 60f, MinSat = 0.2f }.GenerateFrom(img);
        Assert.True(m[0] < 0.05f);
    }

    [Fact]
    public void ColorRangeMask_ViaMaskedOp_Registry()
    {
        var reg = EditOpRegistry.CreateDefault();
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
            ["mask"] = ColorRangeMask.Type, ["hue"] = "0", ["range"] = "30", ["minSat"] = "0.1", ["smooth"] = "0.1",
        };
        var op = reg.Create(MaskedOp.Type, p);
        Assert.NotNull(op);
        // ảnh đỏ -> được áp +1EV; ảnh xanh -> hầu như không.
        var red = Solid(ColorSpace.SrgbToLinear(0.5f), ColorSpace.SrgbToLinear(0.05f), ColorSpace.SrgbToLinear(0.05f), 4, 4);
        float before = red.Pixels[0];
        op!.Apply(red, 1f);
        Assert.True(red.Pixels[0] > before * 1.5f);
    }

    // ---- BrushMask (6.4) ----

    [Fact]
    public void BrushMask_EmptyIsZero()
    {
        var m = new BrushMask().Generate(8, 8);
        foreach (var v in m) Assert.Equal(0f, v);
    }

    [Fact]
    public void BrushMask_CenterStrokeHigherThanCorner()
    {
        var brush = new BrushMask { Radius = 0.25f, Hardness = 0.5f };
        brush.Points.Add((0.5f, 0.5f));
        var m = brush.Generate(33, 33);
        int center = 16 * 33 + 16;
        Assert.True(m[center] > 0.9f);
        Assert.True(m[0] < m[center]);
    }

    [Fact]
    public void BrushMask_PointsRoundTrip()
    {
        var brush = new BrushMask { Radius = 0.1f, Hardness = 0.7f };
        brush.Points.Add((0.2f, 0.3f));
        brush.Points.Add((0.8f, 0.9f));
        var back = BrushMask.FromParams(brush.ToParams());
        Assert.Equal(2, back.Points.Count);
        Assert.Equal(0.2f, back.Points[0].X, 4);
        Assert.Equal(0.9f, back.Points[1].Y, 4);
        Assert.Equal(0.7f, back.Hardness, 4);
    }

    [Fact]
    public void BrushMask_ViaMaskedOp_Registry()
    {
        var reg = EditOpRegistry.CreateDefault();
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
            ["mask"] = BrushMask.Type, ["radius"] = "0.3", ["hardness"] = "0.5",
            ["pts"] = "0.5,0.5",
        };
        var op = reg.Create(MaskedOp.Type, p);
        Assert.NotNull(op);
        var img = Solid(0.25f, 0.25f, 0.25f, 16, 16);
        op!.Apply(img, 1f);
        int center = (8 * 16 + 8) * 4;
        Assert.True(img.Pixels[center] > 0.4f); // tâm sáng lên
        Assert.InRange(img.Pixels[0], 0.24f, 0.30f); // góc gần như không đổi
    }

    // ---- PerspectiveOp (5.4) ----

    [Fact]
    public void Perspective_Identity_ReturnsSame()
    {
        var op = new PerspectiveOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f, 0.5f, 0.5f, 8, 8);
        var r = op.ApplyResize(img, 1f);
        Assert.Same(img, r);
    }

    [Fact]
    public void Perspective_PreservesDimensions()
    {
        var img = Solid(0.5f, 0.4f, 0.3f, 12, 10);
        var r = new PerspectiveOp { Vertical = 0.3f }.ApplyResize(img, 1f);
        Assert.Equal(12, r.Width);
        Assert.Equal(10, r.Height);
    }

    [Fact]
    public void Perspective_CenterRoughlyPreserved()
    {
        // ảnh có tâm sáng, nền tối; sau keystone nhẹ tâm vẫn sáng.
        var img = Solid(0f, 0f, 0f, 21, 21);
        int c = (10 * 21 + 10) * 4;
        img.Pixels[c] = 1f; img.Pixels[c + 1] = 1f; img.Pixels[c + 2] = 1f;
        var r = new PerspectiveOp { Vertical = 0.2f }.ApplyResize(img, 1f);
        // tâm vẫn còn sáng đáng kể (keystone không dịch tâm nhiều).
        Assert.True(r.Pixels[c] > 0.3f);
    }

    [Fact]
    public void Perspective_RoundTripParams()
    {
        var op = new PerspectiveOp { Vertical = 0.4f, Horizontal = -0.2f, Rotate = 3f, Scale = 1.1f };
        var back = PerspectiveOp.FromParams(op.ToParams());
        Assert.Equal(0.4f, back.Vertical, 4);
        Assert.Equal(-0.2f, back.Horizontal, 4);
        Assert.Equal(3f, back.Rotate, 3);
        Assert.Equal(1.1f, back.Scale, 4);
    }

    [Fact]
    public void Perspective_ViaPipeline_Replays()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(PerspectiveOp.Type));
        var pipeline = new EditPipeline(reg);
        var baseImg = Solid(0.3f, 0.3f, 0.3f, 16, 16);
        var ops = new List<EditOperation>
        {
            new EditOperation { OpType = PerspectiveOp.Type, Params = new() { ["vert"] = "0.2", ["scale"] = "1.05" } }
        };
        var result = pipeline.Render(baseImg, ops);
        Assert.Equal(16, result.Width);
        Assert.Equal(16, result.Height);
        // base nguyên vẹn
        Assert.InRange(baseImg.Pixels[0], 0.299f, 0.301f);
    }
}
