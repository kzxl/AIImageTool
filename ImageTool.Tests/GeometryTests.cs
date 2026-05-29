using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class GeometryTests
{
    // Ảnh có pixel góc trên-trái đặc biệt để theo dõi orientation.
    private static LinearImage Marked(int w = 4, int h = 6)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i + 3] = 1f; }
        // đánh dấu pixel (0,0) = đỏ thuần
        img.Pixels[0] = 1f; img.Pixels[1] = 0f; img.Pixels[2] = 0f;
        return img;
    }

    [Fact]
    public void Orientation_Identity_NoChange()
    {
        var op = new OrientationOp();
        Assert.True(op.IsIdentity);
        var img = Marked();
        var r = op.ApplyResize(img, 1f);
        Assert.Same(img, r);
    }

    [Fact]
    public void Rotate90CW_SwapsDimensions()
    {
        var img = Marked(4, 6); // 4x6
        var r = new OrientationOp { Rotate90 = 1 }.ApplyResize(img, 1f);
        Assert.Equal(6, r.Width);
        Assert.Equal(4, r.Height);
    }

    [Fact]
    public void Rotate90CW_MovesTopLeftToTopRight()
    {
        var img = Marked(4, 6);
        var r = new OrientationOp { Rotate90 = 1 }.ApplyResize(img, 1f);
        // pixel đỏ (0,0) sau xoay CW nằm ở góc trên-phải: (W-1, 0) của ảnh mới rộng 6.
        int tr = (0 * r.Width + (r.Width - 1)) * 4;
        Assert.InRange(r.Pixels[tr], 0.99f, 1.01f);     // R
        Assert.InRange(r.Pixels[tr + 1], 0f, 0.01f);    // G
    }

    [Fact]
    public void Rotate360_ReturnsToOriginal()
    {
        var img = Marked(4, 6);
        var r = new OrientationOp { Rotate90 = 4 }.ApplyResize(img, 1f);
        // Rotate90=4 => identity (4%4==0)
        Assert.Same(img, r);
    }

    [Fact]
    public void FlipHorizontal_MovesTopLeftToTopRight()
    {
        var img = Marked(4, 6);
        var r = new OrientationOp { FlipH = true }.ApplyResize(img, 1f);
        Assert.Equal(4, r.Width);
        int tr = (0 * 4 + 3) * 4;
        Assert.InRange(r.Pixels[tr], 0.99f, 1.01f);
    }

    [Fact]
    public void Crop_Identity_NoChange()
    {
        var op = new CropOp();
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void Crop_HalfReducesSize()
    {
        var img = Marked(10, 10);
        var r = new CropOp { X = 0.25f, Y = 0.25f, W = 0.5f, H = 0.5f }.ApplyResize(img, 1f);
        Assert.Equal(5, r.Width);
        Assert.Equal(5, r.Height);
    }

    [Fact]
    public void Crop_RoundTripParams()
    {
        var op = new CropOp { X = 0.1f, Y = 0.2f, W = 0.7f, H = 0.6f, Angle = 5f };
        var back = CropOp.FromParams(op.ToParams());
        Assert.Equal(0.1f, back.X, 4);
        Assert.Equal(0.6f, back.H, 4);
        Assert.Equal(5f, back.Angle, 3);
    }

    [Fact]
    public void Geometry_ViaPipeline_ChangesDimensions()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var baseImg = Marked(8, 12);

        var ops = new List<EditOperation>
        {
            new EditOperation { OpType = OrientationOp.Type, Params = new() { ["rot"] = "1" } }
        };
        var result = pipeline.Render(baseImg, ops);
        Assert.Equal(12, result.Width); // đã xoay
        Assert.Equal(8, result.Height);
        // base nguyên vẹn
        Assert.Equal(8, baseImg.Width);
    }

    [Fact]
    public void Geometry_RegisteredInRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(OrientationOp.Type));
        Assert.True(reg.Has(CropOp.Type));
    }
}
