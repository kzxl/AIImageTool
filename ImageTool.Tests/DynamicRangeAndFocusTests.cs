using System;
using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ExposureFusionTests
{
    private static LinearImage Solid(float v, int w = 32, int h = 32)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // ảnh solid xác định theo giá trị sRGB (well-exposedness đo trên sRGB).
    private static LinearImage SolidSrgb(float srgb, int w = 32, int h = 32)
        => Solid(ColorSpace.SrgbToLinear(srgb), w, h);

    // ảnh nửa trái sáng, nửa phải tối (theo exposure).
    private static LinearImage SplitExposure(float left, float right, int w = 32, int h = 32)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                float v = x < w / 2 ? left : right;
                img.Pixels[p] = img.Pixels[p + 1] = img.Pixels[p + 2] = v; img.Pixels[p + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void SingleImage_ReturnsClone()
    {
        var img = Solid(0.5f);
        var fused = ExposureFusion.Fuse(new[] { img });
        Assert.Equal(img.Width, fused.Width);
        Assert.NotSame(img, fused);
    }

    [Fact]
    public void DifferentSizes_Throws()
    {
        var a = Solid(0.5f, 16, 16);
        var b = Solid(0.5f, 8, 8);
        Assert.Throws<ArgumentException>(() => ExposureFusion.Fuse(new[] { a, b }));
    }

    [Fact]
    public void Fuse_OutputSameSize_NoNaN()
    {
        var under = Solid(0.2f);
        var over = Solid(0.8f);
        var fused = ExposureFusion.Fuse(new[] { under, over });
        Assert.Equal(32, fused.Width);
        Assert.Equal(32, fused.Height);
        foreach (var v in fused.Pixels) Assert.False(float.IsNaN(v) || float.IsInfinity(v));
    }

    [Fact]
    public void Fuse_PicksWellExposedRegions()
    {
        // 2 ảnh SOLID (theo sRGB): A phơi tốt (sRGB 0.5), B tối (sRGB 0.05). Ưu tiên A.
        // levels=1 -> per-pixel weighted blend, cô lập logic trọng số.
        var a = SolidSrgb(0.5f);
        var b = SolidSrgb(0.05f);
        var fused = ExposureFusion.Fuse(new[] { a, b }, new ExposureFusion.Options { PyramidLevels = 1 });
        float fusedSrgb = ColorSpace.LinearToSrgb(fused.Pixels[0]);
        // kết quả (sRGB) phải gần 0.5 (A) hơn là trung bình của 0.5 và 0.05.
        Assert.True(fusedSrgb > 0.4f, $"value(sRGB)={fusedSrgb}");
    }

    [Fact]
    public void Fuse_AvoidsBlownHighlights()
    {
        // A cháy (sRGB 0.98), B phơi tốt (sRGB 0.5) -> ưu tiên B.
        var a = SolidSrgb(0.98f);
        var b = SolidSrgb(0.5f);
        var fused = ExposureFusion.Fuse(new[] { a, b }, new ExposureFusion.Options { PyramidLevels = 1 });
        float fusedSrgb = ColorSpace.LinearToSrgb(fused.Pixels[0]);
        Assert.True(fusedSrgb < 0.7f, $"value(sRGB)={fusedSrgb}");
    }

    [Fact]
    public void Fuse_MultiScale_BalancesWithoutClipping()
    {
        // multi-scale blend: kết quả không còn vùng cháy (0.98) hay tối thui (0.05) như đầu vào.
        var a = SplitExposure(0.5f, 0.98f);
        var b = SplitExposure(0.05f, 0.5f);
        var fused = ExposureFusion.Fuse(new[] { a, b });
        int w = fused.Width, h = fused.Height;
        int rightIdx = (h / 2 * w + 3 * w / 4) * 4;
        // nửa phải không còn cháy như A (0.98).
        Assert.True(fused.Pixels[rightIdx] < 0.95f);
        int leftIdx = (h / 2 * w + w / 4) * 4;
        // nửa trái sáng hơn vùng tối thui của B (0.05).
        Assert.True(fused.Pixels[leftIdx] > 0.05f);
    }

    [Fact]
    public void Fuse_ThreeImages_Works()
    {
        var f = ExposureFusion.Fuse(new[] { Solid(0.2f), Solid(0.5f), Solid(0.8f) });
        Assert.Equal(32, f.Width);
        // ảnh đồng nhất -> kết quả gần 1 trong các mức (không NaN).
        Assert.InRange(f.Pixels[0], 0.1f, 0.9f);
    }
}

public class FocusMeasureTests
{
    private static LinearImage Flat(int w = 32, int h = 32, float v = 0.5f)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // ảnh có cạnh sắc (checkerboard) = nét; ảnh phẳng = mờ.
    private static LinearImage Checker(int w = 32, int h = 32, int cell = 2)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                float v = ((x / cell + y / cell) & 1) == 0 ? 0.9f : 0.1f;
                img.Pixels[p] = img.Pixels[p + 1] = img.Pixels[p + 2] = v; img.Pixels[p + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Sharp_HasHigherVarianceThanFlat()
    {
        double sharp = FocusMeasure.VarianceOfLaplacian(Checker());
        double flat = FocusMeasure.VarianceOfLaplacian(Flat());
        Assert.True(sharp > flat);
        Assert.True(flat < 1e-6);
    }

    [Fact]
    public void Sharp_HasHigherTenengradThanFlat()
    {
        Assert.True(FocusMeasure.Tenengrad(Checker()) > FocusMeasure.Tenengrad(Flat()));
    }

    [Fact]
    public void IsBlurry_DetectsFlat()
    {
        Assert.True(FocusMeasure.IsBlurry(Flat()));
        Assert.False(FocusMeasure.IsBlurry(Checker()));
    }

    [Fact]
    public void FocusMap_HighOnEdges()
    {
        var img = Checker(32, 32, 4);
        var map = FocusMeasure.FocusMap(img);
        Assert.Equal(32 * 32, map.Length);
        float max = 0f; foreach (var v in map) if (v > max) max = v;
        Assert.True(max > 0.5f); // có vùng nét rõ
        foreach (var v in map) Assert.InRange(v, 0f, 1f);
    }
}

public class FocusStackTests
{
    private const int W = 32, H = 32;

    // ảnh nét nửa trái (checker trái, phẳng phải) và ngược lại.
    private static LinearImage HalfSharp(bool leftSharp)
    {
        var img = new LinearImage(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int p = (y * W + x) * 4;
                bool inSharpHalf = leftSharp ? x < W / 2 : x >= W / 2;
                float v;
                if (inSharpHalf) v = ((x + y) & 1) == 0 ? 0.9f : 0.1f; // cạnh sắc
                else v = 0.5f; // phẳng (mờ)
                img.Pixels[p] = img.Pixels[p + 1] = img.Pixels[p + 2] = v; img.Pixels[p + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void SingleImage_ReturnsClone()
    {
        var img = HalfSharp(true);
        var s = FocusStack.Stack(new[] { img });
        Assert.Equal(W, s.Width);
        Assert.NotSame(img, s);
    }

    [Fact]
    public void Stack_KeepsSharpHalfFromEach()
    {
        var a = HalfSharp(leftSharp: true);   // trái nét
        var b = HalfSharp(leftSharp: false);  // phải nét
        var stacked = FocusStack.Stack(new[] { a, b });

        // độ nét của ảnh ghép phải >= mỗi ảnh đơn (lấy nửa nét của cả hai).
        double stackedSharp = FocusMeasure.VarianceOfLaplacian(stacked);
        double aSharp = FocusMeasure.VarianceOfLaplacian(a);
        Assert.True(stackedSharp >= aSharp * 0.9, $"stacked={stackedSharp}, a={aSharp}");
        Assert.Equal(W, stacked.Width);
    }

    [Fact]
    public void DifferentSizes_Throws()
    {
        var a = new LinearImage(16, 16);
        var b = new LinearImage(8, 8);
        Assert.Throws<ArgumentException>(() => FocusStack.Stack(new[] { a, b }));
    }
}
