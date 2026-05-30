using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Shared;

/// <summary>
/// Trích bảng màu chủ đạo bằng K-Means (gộp từ plugin ColorLab cũ vào InfoPanel). Đây là phân
/// tích THUẦN (không phải edit op): thu nhỏ ảnh rồi gom cụm màu. Trả danh sách màu + tỉ lệ %.
/// Deterministic (seed cố định) để test ổn định.
/// </summary>
public static class DominantColors
{
    public sealed class Swatch
    {
        public byte R { get; init; }
        public byte G { get; init; }
        public byte B { get; init; }
        public float Fraction { get; init; }   // tỉ lệ [0..1]
        public string Hex => $"#{R:X2}{G:X2}{B:X2}";
        public string PercentText => (Fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Trích k màu chủ đạo từ file ảnh. Trả rỗng nếu lỗi/ảnh trống.</summary>
    public static List<Swatch> Extract(string imagePath, int k = 6, int maxIterations = 12)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imagePath);
            return Extract(image, k, maxIterations);
        }
        catch (Exception ex)
        {
            AppLog.Warn("DominantColors.Extract", $"{imagePath}: {ex.Message}");
            return new List<Swatch>();
        }
    }

    /// <summary>K-Means trên ảnh đã load (thu nhỏ ≤128px để nhanh). Bỏ pixel trong suốt + gần đen/trắng tuyệt đối.</summary>
    public static List<Swatch> Extract(Image<Rgba32> source, int k = 6, int maxIterations = 12)
    {
        if (k < 1) k = 1;
        int tw = Math.Min(128, source.Width), th = Math.Min(128, source.Height);
        using var small = source.Clone(x => x.Resize(Math.Max(1, tw), Math.Max(1, th)));

        var pts = new List<(float R, float G, float B)>();
        small.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                foreach (ref Rgba32 p in row)
                {
                    if (p.A < 250) continue;
                    if (p.R < 12 && p.G < 12 && p.B < 12) continue;       // gần đen
                    if (p.R > 244 && p.G > 244 && p.B > 244) continue;    // gần trắng
                    pts.Add((p.R / 255f, p.G / 255f, p.B / 255f));
                }
            }
        });
        if (pts.Count == 0) return new List<Swatch>();

        var rnd = new Random(42);
        var centers = pts.OrderBy(_ => rnd.Next()).Take(k).ToList();
        if (centers.Count < k) k = centers.Count;
        var assign = new int[pts.Count];

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // gán cụm gần nhất.
            for (int i = 0; i < pts.Count; i++)
            {
                int best = 0; float bestD = float.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    var ce = centers[c];
                    float dr = pts[i].R - ce.R, dg = pts[i].G - ce.G, db = pts[i].B - ce.B;
                    float d = dr * dr + dg * dg + db * db;
                    if (d < bestD) { bestD = d; best = c; }
                }
                assign[i] = best;
            }
            // cập nhật tâm cụm.
            var sumR = new float[k]; var sumG = new float[k]; var sumB = new float[k]; var cnt = new int[k];
            for (int i = 0; i < pts.Count; i++)
            {
                int c = assign[i]; sumR[c] += pts[i].R; sumG[c] += pts[i].G; sumB[c] += pts[i].B; cnt[c]++;
            }
            bool changed = false;
            for (int c = 0; c < k; c++)
            {
                if (cnt[c] == 0) continue;
                var nc = (sumR[c] / cnt[c], sumG[c] / cnt[c], sumB[c] / cnt[c]);
                if (Math.Abs(nc.Item1 - centers[c].R) > 0.004f || Math.Abs(nc.Item2 - centers[c].G) > 0.004f || Math.Abs(nc.Item3 - centers[c].B) > 0.004f)
                    changed = true;
                centers[c] = nc;
            }
            if (!changed) break;
        }

        // đếm cuối cùng cho tỉ lệ.
        var final = new int[k];
        for (int i = 0; i < pts.Count; i++) final[assign[i]]++;
        int total = pts.Count;

        var result = new List<Swatch>();
        for (int c = 0; c < k; c++)
        {
            if (final[c] == 0) continue;
            result.Add(new Swatch
            {
                R = (byte)Math.Clamp(centers[c].R * 255f, 0, 255),
                G = (byte)Math.Clamp(centers[c].G * 255f, 0, 255),
                B = (byte)Math.Clamp(centers[c].B * 255f, 0, 255),
                Fraction = (float)final[c] / total
            });
        }
        return result.OrderByDescending(s => s.Fraction).ToList();
    }
}
