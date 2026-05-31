using System;
using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class HomographyTests
{
    private static Homography.Pt P(double x, double y) => new Homography.Pt(x, y);

    // Ap 1 homography "su that" cho diem.
    private static List<Homography.Pt> Map(double[] h, List<Homography.Pt> src)
    {
        var dst = new List<Homography.Pt>();
        foreach (var p in src) dst.Add(Homography.Apply(h, p.X, p.Y));
        return dst;
    }

    [Fact]
    public void Apply_Translation()
    {
        double[] h = { 1, 0, 10, 0, 1, 20, 0, 0, 1 };
        var p = Homography.Apply(h, 5, 5);
        Assert.Equal(15, p.X, 6);
        Assert.Equal(25, p.Y, 6);
    }

    [Fact]
    public void Dlt_RecoversAffine()
    {
        // Affine: scale + xoay + tinh tien.
        double[] truth = { 1.2, -0.3, 40, 0.1, 0.9, -15, 0, 0, 1 };
        var src = new List<Homography.Pt> { P(0, 0), P(100, 0), P(0, 100), P(100, 100), P(50, 25) };
        var dst = Map(truth, src);

        var H = Homography.EstimateDlt(src, dst);
        Assert.NotNull(H);
        // Kiem tra qua reprojection thay vi so sanh phan tu (do scale tu do). Sai so < 0.1px.
        foreach (var s in src)
        {
            var expected = Homography.Apply(truth, s.X, s.Y);
            var got = Homography.Apply(H!, s.X, s.Y);
            Assert.Equal(expected.X, got.X, 1);
            Assert.Equal(expected.Y, got.Y, 1);
        }
    }

    [Fact]
    public void Dlt_RecoversPerspective()
    {
        double[] truth = { 1.0, 0.05, 10, -0.02, 1.1, 5, 0.0005, 0.0003, 1 };
        var src = new List<Homography.Pt> { P(0, 0), P(200, 0), P(0, 200), P(200, 200), P(100, 50), P(30, 170) };
        var dst = Map(truth, src);

        var H = Homography.EstimateDlt(src, dst);
        Assert.NotNull(H);
        foreach (var s in src)
        {
            var e = Homography.Apply(truth, s.X, s.Y);
            var g = Homography.Apply(H!, s.X, s.Y);
            Assert.True(System.Math.Abs(e.X - g.X) < 0.5, $"X off {e.X}->{g.X}");
            Assert.True(System.Math.Abs(e.Y - g.Y) < 0.5, $"Y off {e.Y}->{g.Y}");
        }
    }

    [Fact]
    public void Dlt_TooFewPoints_ReturnsNull()
    {
        var src = new List<Homography.Pt> { P(0, 0), P(1, 0), P(0, 1) };
        var dst = new List<Homography.Pt> { P(0, 0), P(1, 0), P(0, 1) };
        Assert.Null(Homography.EstimateDlt(src, dst));
    }

    [Fact]
    public void Ransac_IgnoresOutliers()
    {
        double[] truth = { 1.1, 0.02, 25, -0.03, 1.05, -10, 0.0002, 0.0001, 1 };
        var src = new List<Homography.Pt>();
        var dst = new List<Homography.Pt>();
        var rng = new Random(1);
        // 20 inlier.
        for (int i = 0; i < 20; i++)
        {
            var s = P(rng.Next(300), rng.Next(300));
            src.Add(s); dst.Add(Homography.Apply(truth, s.X, s.Y));
        }
        // 8 outlier (lech ngau nhien lon).
        for (int i = 0; i < 8; i++)
        {
            var s = P(rng.Next(300), rng.Next(300));
            src.Add(s); dst.Add(P(rng.Next(300) + 500, rng.Next(300) + 500));
        }

        var H = Homography.EstimateRansac(src, dst, out bool[] inliers, 2.0, 800);
        Assert.NotNull(H);
        // It nhat ~20 inlier duoc nhan dien.
        int count = 0; foreach (var b in inliers) if (b) count++;
        Assert.True(count >= 18, $"chỉ {count} inlier");

        // Reprojection cho inlier dau (chac chan la inlier) chinh xac.
        var e = Homography.Apply(truth, src[0].X, src[0].Y);
        var g = Homography.Apply(H!, src[0].X, src[0].Y);
        Assert.True(System.Math.Abs(e.X - g.X) < 1.0, $"X off {e.X}->{g.X}");
        Assert.True(System.Math.Abs(e.Y - g.Y) < 1.0, $"Y off {e.Y}->{g.Y}");
    }

    [Fact]
    public void Invert3x3_RoundTrips()
    {
        double[] m = { 1.2, 0.1, 5, -0.2, 0.9, 3, 0.001, 0.002, 1 };
        var inv = Homography.Invert3x3(m);
        var prod = Homography.Mul3x3(m, inv);
        // ~ I.
        Assert.Equal(1, prod[0], 4); Assert.Equal(0, prod[1], 4);
        Assert.Equal(1, prod[4], 4); Assert.Equal(1, prod[8], 4);
    }
}
