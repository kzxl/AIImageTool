using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class HealingOpTests
{
    // Ảnh nền xám đồng nhất + 1 "vết" đỏ tại 1 vùng để test heal/clone.
    private static LinearImage WithSpot(int w = 32, int h = 32)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.5f; img.Pixels[i + 1] = 0.5f; img.Pixels[i + 2] = 0.5f; img.Pixels[i + 3] = 1f; }
        // vết đỏ quanh (8,8) bán kính 3.
        for (int y = 5; y <= 11; y++)
            for (int x = 5; x <= 11; x++)
            {
                int o = (y * w + x) * 4;
                img.Pixels[o] = 1f; img.Pixels[o + 1] = 0f; img.Pixels[o + 2] = 0f;
            }
        return img;
    }

    [Fact]
    public void Identity_WhenNoSpots()
    {
        var op = new HealingOp();
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void Clone_CopiesSourceOverTarget()
    {
        var img = WithSpot(32, 32);
        // đích = vết đỏ (8/31), nguồn = vùng xám (24/31), bán kính ~0.12 (≈4px).
        var op = new HealingOp { Mode = HealingOp.HealMode.Clone };
        op.Spots.Add(new HealingOp.Spot(8f / 31f, 8f / 31f, 24f / 31f, 24f / 31f, 4f / 32f));
        op.Apply(img, 1f);
        // tâm vết đỏ giờ phải gần xám (đã clone đè), không còn đỏ rực.
        int o = (8 * 32 + 8) * 4;
        Assert.True(img.Pixels[o] < 0.8f);          // R giảm mạnh
        Assert.True(img.Pixels[o + 1] > 0.2f);      // G tăng (xám hoá)
    }

    [Fact]
    public void Heal_RemovesSpot()
    {
        var img = WithSpot(32, 32);
        var op = new HealingOp { Mode = HealingOp.HealMode.Heal };
        op.Spots.Add(new HealingOp.Spot(8f / 31f, 8f / 31f, 24f / 31f, 24f / 31f, 5f / 32f));
        op.Apply(img, 1f);
        int o = (8 * 32 + 8) * 4;
        // sau heal, vùng đó xấp xỉ xám nền (R≈G≈B).
        float r = img.Pixels[o], g = img.Pixels[o + 1], b = img.Pixels[o + 2];
        Assert.True(System.MathF.Abs(r - g) < 0.25f);
    }

    [Fact]
    public void RoundTrip_Spots()
    {
        var op = new HealingOp { Mode = HealingOp.HealMode.Clone };
        op.Spots.Add(new HealingOp.Spot(0.1f, 0.2f, 0.3f, 0.4f, 0.05f));
        op.Spots.Add(new HealingOp.Spot(0.5f, 0.6f, 0.7f, 0.8f, 0.02f));
        var back = HealingOp.FromParams(op.ToParams());
        Assert.Equal(2, back.Spots.Count);
        Assert.Equal(HealingOp.HealMode.Clone, back.Mode);
        Assert.Equal(0.3f, back.Spots[0].Sx, 4);
        Assert.Equal(0.8f, back.Spots[1].Sy, 4);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(HealingOp.Type));
    }
}

public class LensCorrectionOpTests
{
    private static LinearImage Gradient(int w = 32, int h = 32)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                img.Pixels[o] = x / (float)(w - 1);
                img.Pixels[o + 1] = y / (float)(h - 1);
                img.Pixels[o + 2] = 0.5f; img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Identity_NoParams()
    {
        var op = new LensCorrectionOp();
        Assert.True(op.IsIdentity);
        var img = Gradient();
        float before = img.Pixels[(16 * 32 + 16) * 4];
        op.Apply(img, 1f);
        Assert.Equal(before, img.Pixels[(16 * 32 + 16) * 4], 4);
    }

    [Fact]
    public void Distortion_PreservesCenter()
    {
        var img = Gradient(33, 33);
        int c = (16 * 33 + 16) * 4;
        float cr = img.Pixels[c], cg = img.Pixels[c + 1];
        new LensCorrectionOp { K1 = 0.2f }.Apply(img, 1f);
        // tâm (r=0) gần như không đổi vì factor=1 tại tâm.
        Assert.InRange(img.Pixels[c], cr - 0.05f, cr + 0.05f);
        Assert.InRange(img.Pixels[c + 1], cg - 0.05f, cg + 0.05f);
    }

    [Fact]
    public void Distortion_PreservesDimensions()
    {
        var img = Gradient(40, 30);
        new LensCorrectionOp { K1 = -0.15f }.Apply(img, 1f);
        Assert.Equal(40, img.Width);
        Assert.Equal(30, img.Height);
    }

    [Fact]
    public void VignetteCorrection_BrightensCorners()
    {
        var img = Gradient(32, 32);
        int corner = (0 * 32 + 0) * 4; // góc trên-trái
        // đặt giá trị xác định ở góc.
        img.Pixels[corner] = 0.4f; img.Pixels[corner + 1] = 0.4f; img.Pixels[corner + 2] = 0.4f;
        float before = img.Pixels[corner];
        new LensCorrectionOp { VignetteCorrection = 0.5f }.Apply(img, 1f);
        Assert.True(img.Pixels[corner] > before, "góc phải sáng lên");
    }

    [Fact]
    public void RoundTrip()
    {
        var op = new LensCorrectionOp { K1 = 0.1f, K2 = -0.05f, VignetteCorrection = 0.3f };
        var back = LensCorrectionOp.FromParams(op.ToParams());
        Assert.Equal(0.1f, back.K1, 4);
        Assert.Equal(-0.05f, back.K2, 4);
        Assert.Equal(0.3f, back.VignetteCorrection, 4);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(LensCorrectionOp.Type));
    }
}
