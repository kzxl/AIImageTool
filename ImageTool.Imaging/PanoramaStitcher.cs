using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

// Ghep panorama 2 anh (#4c): detect+match+homography -> warp ve canvas chung -> feather blend.
public static class PanoramaStitcher
{
    public sealed class Result
    {
        public LinearImage? Image { get; init; }
        public int MatchCount { get; init; }
        public int InlierCount { get; init; }
        public bool Success => Image != null;
        public string? Error { get; init; }
    }

    /// <summary>
    /// Ghép <paramref name="imgB"/> vào hệ toạ độ của <paramref name="imgA"/>. imgA giữ nguyên, imgB
    /// được warp bằng homography ước lượng từ feature match. Trả ảnh panorama (canvas bao cả hai) +
    /// thống kê. Thất bại (ít match) -> Error.
    /// </summary>
    public static Result Stitch(LinearImage imgA, LinearImage imgB,
        int maxCorners = 500, float nccThreshold = 0.7f, double ransacThreshold = 3.0)
    {
        if (imgA == null || imgB == null) return new Result { Error = "Ảnh null." };

        var gA = FeatureMatcher.Gray(imgA);
        var gB = FeatureMatcher.Gray(imgB);
        var cA = FeatureMatcher.DetectHarris(gA, imgA.Width, imgA.Height, maxCorners, 10);
        var cB = FeatureMatcher.DetectHarris(gB, imgB.Width, imgB.Height, maxCorners, 10);

        // Match B->A (tìm H sao cho H * pB ~ pA: warp B vào hệ A).
        var matches = FeatureMatcher.MatchNcc(gB, imgB.Width, imgB.Height, cB,
                                              gA, imgA.Width, imgA.Height, cA, 7, nccThreshold);
        if (matches.Count < 8)
            return new Result { MatchCount = matches.Count, Error = $"Quá ít điểm khớp ({matches.Count}). Ảnh cần vùng chồng lấn." };

        var src = new List<Homography.Pt>(matches.Count);
        var dst = new List<Homography.Pt>(matches.Count);
        foreach (var m in matches)
        {
            src.Add(new Homography.Pt(m.X1, m.Y1)); // điểm trên B
            dst.Add(new Homography.Pt(m.X2, m.Y2)); // điểm tương ứng trên A
        }

        var H = Homography.EstimateRansac(src, dst, out bool[] inliers, ransacThreshold, 1000);
        if (H == null) return new Result { MatchCount = matches.Count, Error = "Không ước lượng được homography." };
        int inlierCount = 0; foreach (var b in inliers) if (b) inlierCount++;
        if (inlierCount < 6)
            return new Result { MatchCount = matches.Count, InlierCount = inlierCount, Error = $"Quá ít inlier ({inlierCount})." };

        // Tính canvas: gồm khung A [0..wA,0..hA] + 4 góc B đã warp bằng H.
        int wA = imgA.Width, hA = imgA.Height, wB = imgB.Width, hB = imgB.Height;
        double minX = 0, minY = 0, maxX = wA, maxY = hA;
        foreach (var (cx, cy) in new[] { (0.0, 0.0), (wB, 0.0), (0.0, (double)hB), ((double)wB, (double)hB) })
        {
            var p = Homography.Apply(H, cx, cy);
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }

        int outW = (int)Math.Ceiling(maxX - minX);
        int outH = (int)Math.Ceiling(maxY - minY);
        // Giới hạn an toàn (tránh canvas khổng lồ do homography xấu).
        if (outW <= 0 || outH <= 0 || (long)outW * outH > 80_000_000L)
            return new Result { MatchCount = matches.Count, InlierCount = inlierCount, Error = "Canvas không hợp lệ (homography xấu?)." };

        double offX = -minX, offY = -minY;
        var canvas = new LinearImage(outW, outH);
        var wAcc = new float[outW * outH]; // tổng trọng số cho blend

        // Vẽ A (đặt tại offset, trọng số feather theo khoảng cách tới mép A).
        SplatDirect(imgA, canvas, wAcc, offX, offY);

        // Vẽ B: với mỗi pixel canvas, inverse-map qua H^-1 (sau khi bù offset) về toạ độ B.
        double[] Hinv = Homography.Invert3x3(H);
        SplatWarp(imgB, canvas, wAcc, Hinv, offX, offY);

        // Chuẩn hoá theo tổng trọng số.
        var px = canvas.Pixels;
        for (int i = 0; i < outW * outH; i++)
        {
            float wsum = wAcc[i];
            int o = i * 4;
            if (wsum > 1e-6f)
            {
                float inv = 1f / wsum;
                px[o] *= inv; px[o + 1] *= inv; px[o + 2] *= inv;
                px[o + 3] = 1f;
            }
            else px[o + 3] = 0f; // ngoài cả hai ảnh -> trong suốt
        }

        return new Result { Image = canvas, MatchCount = matches.Count, InlierCount = inlierCount };
    }

    // Feather weight: nhỏ ở mép ảnh, lớn ở giữa (giảm seam khi blend).
    private static float FeatherWeight(int x, int y, int w, int h)
    {
        float fx = Math.Min(x, w - 1 - x) + 1f;
        float fy = Math.Min(y, h - 1 - y) + 1f;
        return Math.Min(fx, fy);
    }

    // Đặt ảnh src vào canvas tại (offX,offY) cộng dồn theo feather weight.
    private static void SplatDirect(LinearImage src, LinearImage canvas, float[] wAcc, double offX, double offY)
    {
        int w = src.Width, h = src.Height, cw = canvas.Width, ch = canvas.Height;
        var sp = src.Pixels; var cp = canvas.Pixels;
        for (int y = 0; y < h; y++)
        {
            int cy = (int)Math.Round(y + offY);
            if (cy < 0 || cy >= ch) continue;
            for (int x = 0; x < w; x++)
            {
                int cx = (int)Math.Round(x + offX);
                if (cx < 0 || cx >= cw) continue;
                float ww = FeatherWeight(x, y, w, h);
                int so = (y * w + x) * 4;
                int co = (cy * cw + cx) * 4;
                cp[co] += sp[so] * ww; cp[co + 1] += sp[so + 1] * ww; cp[co + 2] += sp[so + 2] * ww;
                wAcc[cy * cw + cx] += ww;
            }
        }
    }

    // Warp ảnh src vào canvas: với mỗi pixel canvas, map ngược qua Hinv (đã bù offset) -> lấy mẫu bilinear.
    private static void SplatWarp(LinearImage src, LinearImage canvas, float[] wAcc,
        double[] Hinv, double offX, double offY)
    {
        int w = src.Width, h = src.Height, cw = canvas.Width, ch = canvas.Height;
        var sp = src.Pixels; var cp = canvas.Pixels;
        for (int cy = 0; cy < ch; cy++)
            for (int cx = 0; cx < cw; cx++)
            {
                // toạ độ trong hệ A (bỏ offset) -> map về B qua Hinv.
                double ax = cx - offX, ay = cy - offY;
                var b = Homography.Apply(Hinv, ax, ay);
                float bx = (float)b.X, by = (float)b.Y;
                if (bx < 0 || by < 0 || bx > w - 1 || by > h - 1) continue;

                int x0 = (int)bx, y0 = (int)by;
                int x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
                float tx = bx - x0, ty = by - y0;
                float ww = FeatherWeight(x0, y0, w, h);

                int co = (cy * cw + cx) * 4;
                for (int c = 0; c < 3; c++)
                {
                    float p00 = sp[(y0 * w + x0) * 4 + c], p10 = sp[(y0 * w + x1) * 4 + c];
                    float p01 = sp[(y1 * w + x0) * 4 + c], p11 = sp[(y1 * w + x1) * 4 + c];
                    float top = p00 + (p10 - p00) * tx;
                    float bot = p01 + (p11 - p01) * tx;
                    cp[co + c] += (top + (bot - top) * ty) * ww;
                }
                wAcc[cy * cw + cx] += ww;
            }
    }
}
