using System.Collections.Generic;
using System.Linq;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class LiquifyOpTests
{
    // Ảnh gradient ngang để theo dõi dịch chuyển: R tăng dần theo x, alpha = 1.
    private static LinearImage Gradient(int w = 32, int h = 32)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = img.Offset(x, y);
                float v = (float)x / (w - 1);
                img.Pixels[o] = v; img.Pixels[o + 1] = v; img.Pixels[o + 2] = v; img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Identity_WhenNoWarps()
    {
        var op = new LiquifyOp();
        Assert.True(op.IsIdentity);
        var img = Gradient();
        Assert.Same(img, op.ApplyResize(img, 1f));
    }

    [Fact]
    public void Identity_WhenZeroDisplacement()
    {
        var op = new LiquifyOp { Warps = { new LiquifyOp.Warp { Cx = 0.5f, Cy = 0.5f, Dx = 0f, Dy = 0f, Radius = 0.3f } } };
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void Warp_KeepsDimensions()
    {
        var img = Gradient(40, 24);
        var op = new LiquifyOp { Warps = { new LiquifyOp.Warp { Cx = 0.5f, Cy = 0.5f, Dx = 0.1f, Dy = 0f, Radius = 0.4f } } };
        var r = op.ApplyResize(img, 1f);
        Assert.Equal(40, r.Width);
        Assert.Equal(24, r.Height);
    }

    [Fact]
    public void Warp_ShiftsContentAtCenter()
    {
        // Gradient ngang: dịch +x quanh tâm => giá trị tại tâm phải GIẢM (kéo nội dung từ bên trái sang).
        var img = Gradient(64, 64);
        int cxp = 32, cyp = 32;
        float before = img.GetPixel(cxp, cyp).R;
        var op = new LiquifyOp { Warps = { new LiquifyOp.Warp { Cx = 0.5f, Cy = 0.5f, Dx = 0.15f, Dy = 0f, Radius = 0.45f } } };
        var r = op.ApplyResize(img, 1f);
        float after = r.GetPixel(cxp, cyp).R;
        Assert.True(after < before - 0.02f, $"expected center value to drop, before={before} after={after}");
    }

    [Fact]
    public void Warp_LeavesEdgesUntouched()
    {
        // Handle nhỏ ở giữa không được ảnh hưởng góc ảnh.
        var img = Gradient(64, 64);
        var op = new LiquifyOp { Warps = { new LiquifyOp.Warp { Cx = 0.5f, Cy = 0.5f, Dx = 0.2f, Dy = 0.2f, Radius = 0.2f } } };
        var r = op.ApplyResize(img, 1f);
        Assert.Equal(img.GetPixel(0, 0).R, r.GetPixel(0, 0).R, 4);
        Assert.Equal(img.GetPixel(63, 0).R, r.GetPixel(63, 0).R, 4);
        Assert.Equal(img.GetPixel(0, 63).R, r.GetPixel(0, 63).R, 4);
    }

    [Fact]
    public void Warp_NoTransparentHoles()
    {
        // Lấy mẫu kẹp về mép => alpha luôn = 1 (không thủng).
        var img = Gradient(48, 48);
        var op = new LiquifyOp { Warps = { new LiquifyOp.Warp { Cx = 0.3f, Cy = 0.3f, Dx = -0.4f, Dy = -0.4f, Radius = 0.5f } } };
        var r = op.ApplyResize(img, 1f);
        for (int i = 3; i < r.Pixels.Length; i += 4)
            Assert.Equal(1f, r.Pixels[i], 4);
    }

    [Fact]
    public void RoundTrip_Params()
    {
        var op = new LiquifyOp
        {
            Iterations = 4,
            Warps =
            {
                new LiquifyOp.Warp { Cx = 0.25f, Cy = 0.75f, Dx = 0.1f, Dy = -0.2f, Radius = 0.33f },
                new LiquifyOp.Warp { Cx = 0.6f, Cy = 0.4f, Dx = -0.05f, Dy = 0.08f, Radius = 0.2f },
            }
        };
        var back = LiquifyOp.FromParams(op.ToParams());
        Assert.Equal(4, back.Iterations);
        Assert.Equal(2, back.Warps.Count);
        Assert.Equal(0.25f, back.Warps[0].Cx, 4);
        Assert.Equal(0.75f, back.Warps[0].Cy, 4);
        Assert.Equal(-0.2f, back.Warps[0].Dy, 4);
        Assert.Equal(0.33f, back.Warps[0].Radius, 4);
        Assert.Equal(0.6f, back.Warps[1].Cx, 4);
        Assert.Equal(0.08f, back.Warps[1].Dy, 4);
    }

    [Fact]
    public void RegisteredInRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(LiquifyOp.Type));
        var op = reg.Create(LiquifyOp.Type, new Dictionary<string, string>
        {
            ["warps"] = "0.5,0.5,0.1,0,0.4",
            ["iters"] = "3",
        });
        Assert.IsType<LiquifyOp>(op);
    }

    [Fact]
    public void ViaPipeline_AppliesWarp()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var baseImg = Gradient(64, 64);
        var ops = new List<EditOperation>
        {
            new EditOperation
            {
                OpType = LiquifyOp.Type,
                Params = new() { ["warps"] = "0.5,0.5,0.18,0,0.45", ["iters"] = "3" }
            }
        };
        var result = pipeline.Render(baseImg, ops);
        Assert.Equal(64, result.Width);
        // Khác ảnh gốc tại tâm.
        Assert.NotEqual(baseImg.GetPixel(32, 32).R, result.GetPixel(32, 32).R, 3);
        // Base nguyên vẹn (phi phá hủy).
        Assert.Equal((float)32 / 63, baseImg.GetPixel(32, 32).R, 3);
    }

    [Fact]
    public void ScaleInvariant_ProxyMatchesFull()
    {
        // Warp chuẩn hoá => kết quả tại vị trí tương đối giống nhau giữa 2 độ phân giải.
        var full = Gradient(80, 80);
        var half = Gradient(40, 40);
        var op = new LiquifyOp { Warps = { new LiquifyOp.Warp { Cx = 0.5f, Cy = 0.5f, Dx = 0.2f, Dy = 0f, Radius = 0.4f } } };
        var rFull = op.ApplyResize(full, 1f);
        var rHalf = op.ApplyResize(half, 0.5f);
        // Tâm: cả 2 phải dịch theo cùng hướng (giá trị giảm so với gradient gốc tại tâm).
        Assert.True(rFull.GetPixel(40, 40).R < 0.5f);
        Assert.True(rHalf.GetPixel(20, 20).R < 0.5f);
        // Sai khác nhỏ giữa 2 tỉ lệ.
        Assert.Equal(rFull.GetPixel(40, 40).R, rHalf.GetPixel(20, 20).R, 1);
    }

    [Fact]
    public void EmptyWarpString_ParsesToNoWarps()
    {
        var op = LiquifyOp.FromParams(new Dictionary<string, string> { ["warps"] = "" });
        Assert.Empty(op.Warps);
        Assert.True(op.IsIdentity);
    }
}
