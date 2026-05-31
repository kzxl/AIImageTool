using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

// Phat hien goc Harris + ghep cap bang NCC (normalized cross-correlation) cho panorama (#4b).
public static class FeatureMatcher
{
    public readonly struct Corner
    {
        public readonly int X, Y;
        public readonly float Score;
        public Corner(int x, int y, float score) { X = x; Y = y; Score = score; }
    }

    public readonly struct Match
    {
        public readonly int X1, Y1, X2, Y2;
        public readonly float Ncc;
        public Match(int x1, int y1, int x2, int y2, float ncc) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Ncc = ncc; }
    }

    /// <summary>Trích luminance sRGB (0..1) ra mảng phẳng W*H.</summary>
    public static float[] Gray(LinearImage img)
    {
        int n = img.PixelCount;
        var g = new float[n];
        var px = img.Pixels;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            float lum = 0.2126f * px[o] + 0.7152f * px[o + 1] + 0.0722f * px[o + 2];
            g[i] = ColorSpace.LinearToSrgb(lum);
        }
        return g;
    }

    /// <summary>
    /// Phát hiện góc Harris trên ảnh xám (W×H). Trả tối đa <paramref name="maxCorners"/> góc mạnh nhất,
    /// đã giãn cách tối thiểu <paramref name="minDist"/> px (non-max suppression theo lưới).
    /// </summary>
    public static List<Corner> DetectHarris(float[] gray, int w, int h,
        int maxCorners = 400, int minDist = 12, float k = 0.04f)
    {
        // Gradient Sobel.
        var ix = new float[w * h];
        var iy = new float[w * h];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int o = y * w + x;
                float gx = (gray[o - w + 1] + 2 * gray[o + 1] + gray[o + w + 1])
                         - (gray[o - w - 1] + 2 * gray[o - 1] + gray[o + w - 1]);
                float gy = (gray[o + w - 1] + 2 * gray[o + w] + gray[o + w + 1])
                         - (gray[o - w - 1] + 2 * gray[o - w] + gray[o - w + 1]);
                ix[o] = gx; iy[o] = gy;
            }

        // Harris response R = det(M) - k*trace(M)^2 trên cửa sổ 3x3 (tổng Ixx/Iyy/Ixy).
        var resp = new float[w * h];
        for (int y = 2; y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
                float sxx = 0, syy = 0, sxy = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int o = (y + dy) * w + (x + dx);
                        sxx += ix[o] * ix[o]; syy += iy[o] * iy[o]; sxy += ix[o] * iy[o];
                    }
                float det = sxx * syy - sxy * sxy;
                float trace = sxx + syy;
                resp[y * w + x] = det - k * trace * trace;
            }

        // Non-max suppression theo lưới ô minDist: giữ góc mạnh nhất mỗi ô.
        var corners = new List<Corner>();
        int cells = minDist;
        for (int gy = 0; gy < h; gy += cells)
            for (int gx = 0; gx < w; gx += cells)
            {
                float best = 0; int bx = -1, by = -1;
                int ey = Math.Min(h, gy + cells), ex = Math.Min(w, gx + cells);
                for (int y = gy; y < ey; y++)
                    for (int x = gx; x < ex; x++)
                    {
                        float r = resp[y * w + x];
                        if (r > best) { best = r; bx = x; by = y; }
                    }
                if (bx >= 0 && best > 0) corners.Add(new Corner(bx, by, best));
            }

        corners.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (corners.Count > maxCorners) corners.RemoveRange(maxCorners, corners.Count - maxCorners);
        return corners;
    }

    /// <summary>
    /// Ghép cặp corner giữa 2 ảnh xám bằng NCC trên patch (2*patch+1)². Chỉ giữ cặp NCC ≥ threshold và
    /// thỏa "mutual best" (a là tốt nhất cho b và ngược lại) để loại ghép sai. Trả danh sách Match.
    /// </summary>
    public static List<Match> MatchNcc(
        float[] g1, int w1, int h1, IReadOnlyList<Corner> c1,
        float[] g2, int w2, int h2, IReadOnlyList<Corner> c2,
        int patch = 7, float threshold = 0.7f)
    {
        // Tiền tính patch chuẩn hoá (mean-subtracted, unit norm) cho mỗi corner.
        var p1 = BuildPatches(g1, w1, h1, c1, patch);
        var p2 = BuildPatches(g2, w2, h2, c2, patch);

        int n1 = c1.Count, n2 = c2.Count;
        var best2For1 = new int[n1];
        var bestScore1 = new float[n1];
        for (int i = 0; i < n1; i++) { best2For1[i] = -1; bestScore1[i] = threshold; }
        var best1For2 = new int[n2];
        var bestScore2 = new float[n2];
        for (int j = 0; j < n2; j++) { best1For2[j] = -1; bestScore2[j] = threshold; }

        for (int i = 0; i < n1; i++)
        {
            if (p1[i] == null) continue;
            for (int j = 0; j < n2; j++)
            {
                if (p2[j] == null) continue;
                float ncc = Dot(p1[i]!, p2[j]!);
                if (ncc > bestScore1[i]) { bestScore1[i] = ncc; best2For1[i] = j; }
                if (ncc > bestScore2[j]) { bestScore2[j] = ncc; best1For2[j] = i; }
            }
        }

        var matches = new List<Match>();
        for (int i = 0; i < n1; i++)
        {
            int j = best2For1[i];
            if (j >= 0 && best1For2[j] == i) // mutual best
                matches.Add(new Match(c1[i].X, c1[i].Y, c2[j].X, c2[j].Y, bestScore1[i]));
        }
        return matches;
    }

    private static float[]?[] BuildPatches(float[] g, int w, int h, IReadOnlyList<Corner> cs, int patch)
    {
        int size = 2 * patch + 1;
        var result = new float[cs.Count][];
        for (int idx = 0; idx < cs.Count; idx++)
        {
            int cx = cs[idx].X, cy = cs[idx].Y;
            if (cx - patch < 0 || cy - patch < 0 || cx + patch >= w || cy + patch >= h) { result[idx] = null!; continue; }
            var p = new float[size * size];
            int k = 0; double mean = 0;
            for (int dy = -patch; dy <= patch; dy++)
                for (int dx = -patch; dx <= patch; dx++)
                { float v = g[(cy + dy) * w + (cx + dx)]; p[k++] = v; mean += v; }
            mean /= p.Length;
            double norm = 0;
            for (int i = 0; i < p.Length; i++) { p[i] -= (float)mean; norm += p[i] * (double)p[i]; }
            norm = Math.Sqrt(norm);
            if (norm < 1e-6) { result[idx] = null!; continue; }
            float inv = (float)(1.0 / norm);
            for (int i = 0; i < p.Length; i++) p[i] *= inv;
            result[idx] = p;
        }
        return result;
    }

    private static float Dot(float[] a, float[] b)
    {
        float s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
