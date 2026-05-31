using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class FeatureMatcherTests
{
    // Anh co cac "o vuong" sang tren nen toi -> nhieu goc Harris ro rang.
    private static LinearImage Squares(int w, int h, int shiftX = 0)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        // nen toi
        for (int i = 0; i < p.Length; i += 4) { p[i] = 0.05f; p[i + 1] = 0.05f; p[i + 2] = 0.05f; p[i + 3] = 1f; }
        // dat o vuong sang tai luoi.
        for (int gy = 20; gy < h - 20; gy += 40)
            for (int gx = 20; gx < w - 20; gx += 40)
            {
                int sx = gx + shiftX;
                for (int y = gy; y < gy + 16 && y < h; y++)
                    for (int x = sx; x < sx + 16 && x < w && x >= 0; x++)
                    {
                        int o = (y * w + x) * 4;
                        p[o] = 0.9f; p[o + 1] = 0.9f; p[o + 2] = 0.9f;
                    }
            }
        return img;
    }

    [Fact]
    public void DetectHarris_FindsCorners()
    {
        var img = Squares(200, 200);
        var gray = FeatureMatcher.Gray(img);
        var corners = FeatureMatcher.DetectHarris(gray, 200, 200, 200, 10);
        Assert.True(corners.Count > 10, $"chỉ {corners.Count} góc");
    }

    [Fact]
    public void DetectHarris_FlatImage_FewCorners()
    {
        var img = new LinearImage(100, 100);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.5f; img.Pixels[i + 1] = 0.5f; img.Pixels[i + 2] = 0.5f; img.Pixels[i + 3] = 1f; }
        var gray = FeatureMatcher.Gray(img);
        var corners = FeatureMatcher.DetectHarris(gray, 100, 100);
        Assert.Empty(corners); // ảnh phẳng không có góc
    }

    [Fact]
    public void MatchNcc_MatchesShiftedCopy()
    {
        // Anh 2 = anh 1 dich ngang 10px -> cac corner phai ghep voi do lech ~10.
        var img1 = Squares(240, 200, 0);
        var img2 = Squares(240, 200, 10);
        var g1 = FeatureMatcher.Gray(img1);
        var g2 = FeatureMatcher.Gray(img2);
        var c1 = FeatureMatcher.DetectHarris(g1, 240, 200, 200, 10);
        var c2 = FeatureMatcher.DetectHarris(g2, 240, 200, 200, 10);

        var matches = FeatureMatcher.MatchNcc(g1, 240, 200, c1, g2, 240, 200, c2, 7, 0.6f);
        Assert.True(matches.Count >= 5, $"chỉ {matches.Count} cặp");

        // Đa số cặp có dx ~ +10 (ảnh 2 dịch phải).
        int near10 = 0;
        foreach (var m in matches)
            if (System.Math.Abs((m.X2 - m.X1) - 10) <= 2) near10++;
        Assert.True(near10 >= matches.Count / 2, $"chỉ {near10}/{matches.Count} cặp đúng dịch chuyển");
    }

    [Fact]
    public void Gray_ReturnsCorrectLength()
    {
        var img = new LinearImage(7, 5);
        Assert.Equal(35, FeatureMatcher.Gray(img).Length);
    }
}
