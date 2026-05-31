using System;

namespace ImageTool.Imaging;

/// <summary>
/// Auto-Upright (#6) — ước lượng tham số keystone Vertical/Horizontal cho <see cref="PerspectiveOp"/>
/// bằng cách phân tích độ "hội tụ" của các cạnh gần-dọc và gần-ngang (giống Upright của Lightroom).
///
/// Ý tưởng: nếu các đường thẳng đứng thật bị nghiêng hội tụ (nhà chụp ngước lên), góc nghiêng của
/// chúng tương quan tuyến tính với vị trí ngang -> ước lượng hệ số "lean" dọc. Tương tự cho ngang.
/// Dùng gradient Sobel trên luminance đã làm mờ; gom cạnh gần-dọc (|hướng| gần 90°) và gần-ngang.
///
/// Thuần toán học -> unit test với ảnh có cạnh hội tụ tổng hợp. KHÔNG sửa pixel; chỉ trả gợi ý.
/// </summary>
public static class AutoUpright
{
    public readonly struct Suggestion
    {
        public readonly float Vertical;    // [-1..1] cho PerspectiveOp.Vertical
        public readonly float Horizontal;  // [-1..1]
        public readonly bool HasResult;
        public Suggestion(float v, float h, bool ok) { Vertical = v; Horizontal = h; HasResult = ok; }
    }

    /// <summary>
    /// Ước lượng keystone. Trả HasResult=false nếu không đủ cạnh định hướng rõ. Kết quả gán thẳng vào
    /// PerspectiveOp.Vertical/Horizontal.
    /// </summary>
    public static Suggestion Estimate(LinearImage img)
    {
        if (img == null) return new Suggestion(0, 0, false);
        int w = img.Width, h = img.Height;
        if (w < 16 || h < 16) return new Suggestion(0, 0, false);

        float radius = MathF.Max(2f, MathF.Min(w, h) / 50f);
        float[] lum = GaussianBlur.BlurLuminance(img, radius);

        // Ngưỡng theo magnitude trung bình.
        double sumMag = 0; long n = 0;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                Sobel(lum, w, x, y, out float gx, out float gy);
                sumMag += MathF.Sqrt(gx * gx + gy * gy); n++;
            }
        if (n == 0) return new Suggestion(0, 0, false);
        float thr = (float)(sumMag / n) * 2f;
        if (thr < 1e-6f) return new Suggestion(0, 0, false);

        // Hồi quy tuyến tính: với cạnh gần-DỌC, quan hệ giữa (vị trí x chuẩn hoá) và (độ lệch hướng khỏi 90°)
        // cho biết độ hội tụ -> Vertical lean. Với cạnh gần-NGANG, (vị trí y) vs (lệch khỏi 0°) -> Horizontal.
        double sxV = 0, syV = 0, sxxV = 0, sxyV = 0, wV = 0;
        double sxH = 0, syH = 0, sxxH = 0, sxyH = 0, wH = 0;
        float cx = w * 0.5f, cy = h * 0.5f;

        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                Sobel(lum, w, x, y, out float gx, out float gy);
                float mag2 = gx * gx + gy * gy;
                if (mag2 < thr * thr) continue;
                float weight = MathF.Sqrt(mag2);
                // Hướng cạnh = gradient + 90°.
                float edgeDeg = MathF.Atan2(gy, gx) * 180f / MathF.PI + 90f;
                edgeDeg = Norm180(edgeDeg);

                float devFromVert = DeltaDeg(edgeDeg, 90f);  // gần 0 nếu cạnh dọc
                float devFromHorz = DeltaDeg(edgeDeg, 0f);   // gần 0 nếu cạnh ngang

                if (MathF.Abs(devFromVert) < 30f)
                {
                    double px = (x - cx) / cx;               // [-1..1]
                    sxV += weight * px; syV += weight * devFromVert;
                    sxxV += weight * px * px; sxyV += weight * px * devFromVert; wV += weight;
                }
                else if (MathF.Abs(devFromHorz) < 30f)
                {
                    double py = (y - cy) / cy;
                    sxH += weight * py; syH += weight * devFromHorz;
                    sxxH += weight * py * py; sxyH += weight * py * devFromHorz; wH += weight;
                }
            }

        float vert = Slope(wV, sxV, syV, sxxV, sxyV);
        float horz = Slope(wH, sxH, syH, sxxH, sxyH);

        bool ok = wV > 0 || wH > 0;
        if (!ok) return new Suggestion(0, 0, false);

        // slope (độ lệch hướng / đơn vị vị trí) -> map sang [-1..1] keystone. Hệ số kinh nghiệm.
        float v = Clamp1(vert / 25f);
        float hh = Clamp1(horz / 25f);
        if (MathF.Abs(v) < 0.02f && MathF.Abs(hh) < 0.02f) return new Suggestion(0, 0, false);
        return new Suggestion(v, hh, true);
    }

    // Hệ số góc của hồi quy tuyến tính có trọng số y = a + b·x; trả b (0 nếu suy biến).
    private static float Slope(double sw, double sx, double sy, double sxx, double sxy)
    {
        if (sw <= 0) return 0f;
        double denom = sw * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-9) return 0f;
        return (float)((sw * sxy - sx * sy) / denom);
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

    private static float Norm180(float deg)
    {
        deg %= 180f;
        if (deg < 0) deg += 180f;
        return deg; // [0..180)
    }

    // Độ lệch nhỏ nhất (có dấu) giữa hướng deg (0..180) và target (0 hoặc 90), chu kỳ 180°.
    private static float DeltaDeg(float deg, float target)
    {
        float d = deg - target;
        while (d > 90f) d -= 180f;
        while (d < -90f) d += 180f;
        return d;
    }

    private static float Clamp1(float v) => v < -1f ? -1f : (v > 1f ? 1f : v);
}
