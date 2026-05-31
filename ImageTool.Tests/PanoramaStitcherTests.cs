using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class PanoramaStitcherTests
{
    // Tao "canh" rong co ket cau (cac o vuong ngau nhien deterministic) de co feature ghep duoc.
    private static LinearImage Scene(int w, int h)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        for (int i = 0; i < p.Length; i += 4) { p[i] = 0.1f; p[i + 1] = 0.12f; p[i + 2] = 0.14f; p[i + 3] = 1f; }
        var rng = new Random(42);
        // Rai ~120 o vuong sang vi tri/do sang ngau nhien -> nhieu goc Harris doc dao.
        for (int k = 0; k < 120; k++)
        {
            int bx = rng.Next(w - 14), by = rng.Next(h - 14);
            float v = 0.4f + 0.5f * (float)rng.NextDouble();
            int sz = 6 + rng.Next(8);
            for (int y = by; y < by + sz && y < h; y++)
                for (int x = bx; x < bx + sz && x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    p[o] = v; p[o + 1] = v * 0.9f; p[o + 2] = v * 0.8f;
                }
        }
        return img;
    }

    // Cat 1 vung [x0..x0+cw) cua scene thanh anh con.
    private static LinearImage Crop(LinearImage src, int x0, int cw)
    {
        int h = src.Height;
        var dst = new LinearImage(cw, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < cw; x++)
            {
                int so = (y * src.Width + (x0 + x)) * 4;
                int do_ = (y * cw + x) * 4;
                for (int c = 0; c < 4; c++) dst.Pixels[do_ + c] = src.Pixels[so + c];
            }
        return dst;
    }

    [Fact]
    public void Stitch_TwoOverlappingHalves_Succeeds()
    {
        var scene = Scene(400, 200);
        // A = [0..240), B = [160..400) -> chồng lấn 80px.
        var a = Crop(scene, 0, 240);
        var b = Crop(scene, 160, 240);

        var result = PanoramaStitcher.Stitch(a, b, 600, 0.65f, 3.0);
        Assert.True(result.Success, result.Error ?? "stitch fail");
        Assert.True(result.InlierCount >= 6, $"inlier={result.InlierCount}");
        // Panorama rộng hơn 1 nửa, xấp xỉ bề rộng scene (cho phép sai số biên).
        Assert.True(result.Image!.Width >= 360, $"width={result.Image.Width}");
        Assert.True(result.Image.Width <= 440, $"width={result.Image.Width}");
        // Chiều cao ~200 (dịch ngang thuần, cho phép sai số nhỏ do homography).
        Assert.InRange(result.Image.Height, 196, 212);
    }

    [Fact]
    public void Stitch_NoOverlap_FailsGracefully()
    {
        var a = Scene(160, 160);
        var b = Scene(160, 160); // scene khác hẳn (cùng seed nhưng... thực ra giống) -> dùng seed khác
        // Tạo b hoàn toàn phẳng -> không feature -> ít match.
        for (int i = 0; i < b.Pixels.Length; i += 4) { b.Pixels[i] = 0.5f; b.Pixels[i + 1] = 0.5f; b.Pixels[i + 2] = 0.5f; }

        var result = PanoramaStitcher.Stitch(a, b);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Stitch_NullInput_ReturnsError()
    {
        var result = PanoramaStitcher.Stitch(null!, null!);
        Assert.False(result.Success);
    }

    [Fact]
    public void Stitch_OverlapRegion_BlendsBothSources()
    {
        var scene = Scene(360, 180);
        var a = Crop(scene, 0, 220);
        var b = Crop(scene, 140, 220);
        var result = PanoramaStitcher.Stitch(a, b, 600, 0.65f);
        Assert.True(result.Success, result.Error ?? "fail");
        // Pixel giữa panorama (vùng chồng lấn) phải có alpha=1 (được phủ).
        var img = result.Image!;
        int midX = img.Width / 2, midY = img.Height / 2;
        Assert.Equal(1f, img.Pixels[(midY * img.Width + midX) * 4 + 3], 3);
    }
}
