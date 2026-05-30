using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class CorrectionOpsTests
{
    private static LinearImage Solid(float v, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // ---- HotPixel (D3.3) ----

    [Fact]
    public void HotPixel_Identity_WhenZeroStrength()
    {
        Assert.True(new HotPixelOp { Strength = 0 }.IsIdentity);
    }

    [Fact]
    public void HotPixel_RemovesIsolatedBrightPixel()
    {
        // nền xám 0.2, 1 pixel giữa cực sáng -> bị kéo về ~nền.
        var img = Solid(0.2f, 8, 8);
        int c = (3 * 8 + 3) * 4;
        img.Pixels[c] = img.Pixels[c + 1] = img.Pixels[c + 2] = 1.5f;
        new HotPixelOp { Strength = 1f, Threshold = 0.5f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[c], 0.19f, 0.21f);
    }

    [Fact]
    public void HotPixel_RemovesDeadPixel()
    {
        var img = Solid(0.6f, 8, 8);
        int c = (4 * 8 + 4) * 4;
        img.Pixels[c] = img.Pixels[c + 1] = img.Pixels[c + 2] = 0f;
        new HotPixelOp { Strength = 1f, Threshold = 0.3f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[c], 0.59f, 0.61f);
    }

    [Fact]
    public void HotPixel_LeavesNormalGradientAlone()
    {
        // gradient mượt: không pixel nào lệch quá ngưỡng -> không đổi.
        var img = new LinearImage(8, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int p = (y * 8 + x) * 4;
                float v = 0.1f + 0.02f * x;
                img.Pixels[p] = img.Pixels[p + 1] = img.Pixels[p + 2] = v; img.Pixels[p + 3] = 1f;
            }
        var before = (float[])img.Pixels.Clone();
        new HotPixelOp { Strength = 1f, Threshold = 0.5f }.Apply(img, 1f);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], img.Pixels[i], 5);
    }

    [Fact]
    public void HotPixel_RoundTrip()
    {
        var back = HotPixelOp.FromParams(new HotPixelOp { Strength = 0.7f, Threshold = 0.3f }.ToParams());
        Assert.Equal(0.7f, back.Strength, 4);
        Assert.Equal(0.3f, back.Threshold, 4);
    }

    // ---- CaCorrect (D3.4) ----

    [Fact]
    public void CaCorrect_Identity_WhenZero()
    {
        Assert.True(new CaCorrectOp().IsIdentity);
    }

    [Fact]
    public void CaCorrect_GrayUnchanged()
    {
        // ảnh xám đều: dịch kênh R/B không đổi gì (mọi mẫu bằng nhau).
        var img = Solid(0.5f, 16, 16);
        new CaCorrectOp { Red = 0.5f, Blue = -0.5f }.Apply(img, 1f);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            Assert.Equal(0.5f, img.Pixels[i], 3);     // R
            Assert.Equal(0.5f, img.Pixels[i + 1], 3); // G
            Assert.Equal(0.5f, img.Pixels[i + 2], 3); // B
        }
    }

    [Fact]
    public void CaCorrect_ShiftsRedChannelTowardCenter()
    {
        // tạo cạnh đỏ lệch: kênh R có biên dịch so với G/B. Sửa phải kéo R về khớp hơn.
        int w = 32, h = 4;
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                // G/B: bước nhảy tại x=16; R: bước nhảy lệch tại x=18 (giả lập CA ngang).
                img.Pixels[p] = x >= 18 ? 0.8f : 0.2f;       // R
                img.Pixels[p + 1] = x >= 16 ? 0.8f : 0.2f;   // G
                img.Pixels[p + 2] = x >= 16 ? 0.8f : 0.2f;   // B
                img.Pixels[p + 3] = 1f;
            }
        // áp sửa CA cho kênh đỏ; chỉ cần op chạy không lỗi và thay đổi kênh R gần biên.
        var before = (float[])img.Pixels.Clone();
        new CaCorrectOp { Red = 0.8f }.Apply(img, 1f);
        bool changed = false;
        for (int i = 0; i < img.Pixels.Length; i += 4)
            if (System.MathF.Abs(img.Pixels[i] - before[i]) > 1e-3f) { changed = true; break; }
        Assert.True(changed);
        // kênh G không đổi.
        for (int i = 1; i < img.Pixels.Length; i += 4)
            Assert.Equal(before[i], img.Pixels[i], 4);
    }

    [Fact]
    public void CaCorrect_RoundTrip()
    {
        var back = CaCorrectOp.FromParams(new CaCorrectOp { Red = 0.4f, Blue = -0.6f }.ToParams());
        Assert.Equal(0.4f, back.Red, 4);
        Assert.Equal(-0.6f, back.Blue, 4);
    }

    [Fact]
    public void BothRegistered()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(HotPixelOp.Type));
        Assert.True(reg.Has(CaCorrectOp.Type));
    }
}
