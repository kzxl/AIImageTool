using System;

namespace ImageTool.Imaging;

/// <summary>
/// Tự cân bằng đường chân trời / phương đứng (auto-straighten): ước lượng góc nghiêng dominant bằng
/// TRUNG BÌNH HƯỚNG (circular mean) của hướng cạnh, trọng số theo độ lớn gradient.
///
/// Kỹ thuật: gradient Sobel trên luminance ĐÃ LÀM MỜ (chống alias biên bậc thang). Hướng cạnh đưa về
/// dải [-45..45] theo chu kỳ 90° (cạnh ngang/dọc đều phục vụ straighten). Vì góc tuần hoàn 90°, ta
/// nhân đôi góc rồi lấy vector trung bình (Σ w·e^{i·2θ}) — tránh lỗi "wrap" của trung bình tuyến tính,
/// và tự loại nhiễu (các hướng tản mát triệt tiêu nhau, chỉ hướng dominant còn lại).
///
/// Thuần toán học -> unit test với ảnh biên bậc nghiêng.
/// </summary>
public static class AutoStraighten
{
    /// <summary>
    /// Ước lượng góc nghiêng (độ, [-45..45]). Trả 0 nếu không có hướng dominant rõ. Góc dương = nội dung
    /// nghiêng theo chiều kim đồng hồ; UI đặt straighten = -angle để cân bằng.
    /// </summary>
    public static float EstimateAngle(LinearImage img, float maxAngleDeg = 45f)
    {
        if (img == null) return 0f;
        int w = img.Width, h = img.Height;
        if (w < 8 || h < 8) return 0f;

        // Làm mờ vừa phải (~3px ở full-res; scale-independent vì ta chỉ cần hướng) để de-alias.
        float radius = MathF.Max(2f, MathF.Min(w, h) / 40f);
        float[] lum = GaussianBlur.BlurLuminance(img, radius);

        float maxA = Math.Clamp(maxAngleDeg, 1f, 45f);

        // Pass 1: ngưỡng theo trung bình magnitude.
        double sumMag = 0; long n = 0;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                Sobel(lum, w, x, y, out float gx, out float gy);
                sumMag += MathF.Sqrt(gx * gx + gy * gy);
                n++;
            }
        if (n == 0) return 0f;
        float mean = (float)(sumMag / n);
        float thr = mean * 2f;
        if (thr < 1e-6f) return 0f;

        // Pass 2: vector trung bình của góc-nhân-đôi (chu kỳ 90° -> nhân 2 thành chu kỳ 180° -> ×2 rad).
        double sx = 0, sy = 0; double totalW = 0;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                Sobel(lum, w, x, y, out float gx, out float gy);
                float mag2 = gx * gx + gy * gy;
                if (mag2 < thr * thr) continue;
                float gradAngle = MathF.Atan2(gy, gx) * 180f / MathF.PI;
                float a = Mod90To45(gradAngle - 90f); // hướng cạnh trong [-45..45]
                if (a < -maxA || a > maxA) continue;
                double w2 = MathF.Sqrt(mag2);          // trọng số = |gradient|
                double phi = a * 2.0 * Math.PI / 90.0; // map [-45..45]->[-π..π]
                sx += w2 * Math.Cos(phi);
                sy += w2 * Math.Sin(phi);
                totalW += w2;
            }

        if (totalW <= 0) return 0f;
        // "Độ tập trung" của hướng: |vector|/Σw. Thấp -> không có hướng dominant -> trả 0.
        double concentration = Math.Sqrt(sx * sx + sy * sy) / totalW;
        if (concentration < 0.15) return 0f;

        double meanAngle = Math.Atan2(sy, sx) / 2.0 * 90.0 / Math.PI; // về lại [-45..45]
        float angle = (float)meanAngle;
        if (MathF.Abs(angle) < 0.05f) return 0f;
        return Math.Clamp(angle, -maxA, maxA);
    }

    private static void Sobel(float[] p, int w, int x, int y, out float gx, out float gy)
    {
        int o = y * w + x;
        float tl = p[o - w - 1], tc = p[o - w], tr = p[o - w + 1];
        float ml = p[o - 1], mr = p[o + 1];
        float bl = p[o + w - 1], bc = p[o + w], br = p[o + w + 1];
        gx = (tr + 2 * mr + br) - (tl + 2 * ml + bl);
        gy = (bl + 2 * bc + br) - (tl + 2 * tc + tr);
    }

    private static float Mod90To45(float deg)
    {
        float a = deg % 90f;
        if (a < 0) a += 90f;
        if (a > 45f) a -= 90f;
        return a;
    }
}
