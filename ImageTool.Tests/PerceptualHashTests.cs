using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class PerceptualHashTests
{
    // Anh gradient cheo (co cau truc de hash phan biet).
    private static LinearImage Diagonal(int w, int h, float shift = 0f)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float v = ((x + y) / (float)(w + h) + shift) % 1f;
                v = ColorSpace.SrgbToLinear(System.Math.Clamp(v, 0f, 1f));
                p[o] = v; p[o + 1] = v; p[o + 2] = v; p[o + 3] = 1f;
            }
        return img;
    }

    private static LinearImage Solid(float v, int w, int h)
    {
        var img = new LinearImage(w, h);
        float lin = ColorSpace.SrgbToLinear(v);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = lin; img.Pixels[i + 1] = lin; img.Pixels[i + 2] = lin; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void SameImage_ZeroDistance()
    {
        var img = Diagonal(64, 64);
        ulong h1 = PerceptualHash.DHash(img);
        ulong h2 = PerceptualHash.DHash(img);
        Assert.Equal(0, PerceptualHash.Distance(h1, h2));
        Assert.Equal(1f, PerceptualHash.Similarity(h1, h2), 3);
    }

    [Fact]
    public void ResizedImage_SmallDistance()
    {
        var big = Diagonal(256, 256);
        var small = Diagonal(80, 80); // cùng nội dung, kích thước khác
        ulong hb = PerceptualHash.DHash(big);
        ulong hs = PerceptualHash.DHash(small);
        Assert.True(PerceptualHash.Distance(hb, hs) <= 6, "ảnh cùng nội dung khác kích thước phải gần nhau");
    }

    [Fact]
    public void DifferentImages_LargeDistance()
    {
        var a = Diagonal(64, 64, 0f);
        var b = Solid(0.5f, 64, 64); // ảnh phẳng - khác hẳn gradient
        ulong ha = PerceptualHash.DHash(a);
        ulong hb = PerceptualHash.DHash(b);
        Assert.True(PerceptualHash.Distance(ha, hb) > 10, "ảnh khác hẳn phải xa nhau");
    }

    [Fact]
    public void GroupSimilar_ClustersNearDuplicates()
    {
        // 3 ảnh gần giống (gradient cùng pha) + 1 ảnh khác.
        var hashes = new List<ulong>
        {
            PerceptualHash.DHash(Diagonal(128, 128)),
            PerceptualHash.DHash(Diagonal(120, 120)),
            PerceptualHash.DHash(Diagonal(140, 140)),
            PerceptualHash.DHash(Solid(0.2f, 128, 128)),
        };
        var groups = PerceptualHash.GroupSimilar(hashes, 10);
        Assert.Single(groups);              // 1 nhóm gần-trùng
        Assert.Equal(3, groups[0].Count);   // gồm 3 gradient
        Assert.DoesNotContain(3, groups[0]);// ảnh phẳng không nằm trong nhóm
    }

    [Fact]
    public void GroupSimilar_NoDuplicates_ReturnsEmpty()
    {
        // 3 ảnh có CẤU TRÚC NGANG khác hẳn (dHash so sánh pixel kề ngang):
        //   Horizontal grad (đổi theo y) -> bit toàn 0; Vertical grad (đổi theo x) -> bit toàn 1;
        //   Bars (sọc dọc xen kẽ) -> bit xen kẽ. Ba hash cách xa nhau.
        var hashes = new List<ulong>
        {
            PerceptualHash.DHash(Horizontal(64, 64)),
            PerceptualHash.DHash(Vertical(64, 64)),
            PerceptualHash.DHash(Bars(64, 64)),
        };
        var groups = PerceptualHash.GroupSimilar(hashes, 5);
        Assert.Empty(groups);
    }

    private static LinearImage Bars(int w, int h)
    {
        var img = new LinearImage(w, h); var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            { int o = (y * w + x) * 4; float v = ColorSpace.SrgbToLinear((x / 4) % 2 == 0 ? 0.2f : 0.8f); p[o] = v; p[o + 1] = v; p[o + 2] = v; p[o + 3] = 1f; }
        return img;
    }

    private static LinearImage Vertical(int w, int h)
    {
        var img = new LinearImage(w, h); var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            { int o = (y * w + x) * 4; float v = ColorSpace.SrgbToLinear(x / (float)w); p[o] = v; p[o + 1] = v; p[o + 2] = v; p[o + 3] = 1f; }
        return img;
    }

    private static LinearImage Horizontal(int w, int h)
    {
        var img = new LinearImage(w, h); var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            { int o = (y * w + x) * 4; float v = ColorSpace.SrgbToLinear(y / (float)h); p[o] = v; p[o + 1] = v; p[o + 2] = v; p[o + 3] = 1f; }
        return img;
    }

    [Fact]
    public void AHash_SameImage_ZeroDistance()
    {
        var img = Diagonal(64, 64);
        Assert.Equal(0, PerceptualHash.Distance(PerceptualHash.AHash(img), PerceptualHash.AHash(img)));
    }
}
