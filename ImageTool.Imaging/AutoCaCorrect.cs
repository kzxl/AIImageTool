using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

/// <summary>
/// Tự ước lượng quang sai màu trục (lateral CA): tìm hệ số co/giãn radial cho kênh R và B sao cho
/// khớp nhất với kênh G (G làm chuẩn). CA bên (lateral) khiến R/B lệch dần ra mép -> viền tím/lục.
///
/// Phương pháp: thu thập pixel CẠNH MẠNH ở vùng NGOÀI (rad &gt; 0.4) — nơi CA rõ nhất. Với mỗi ứng viên
/// hệ số <c>k</c> (khớp tham số CaCorrectOp: lấy mẫu R tại bán kính (1+k·rad)), tính tổng (R−G)² đã
/// resample; chọn k cực tiểu. Trả (Red, Blue) đúng quy ước CaCorrectOp (Red = −k_R/0.01).
///
/// Thuần toán học -> unit test với ảnh có CA tổng hợp (R scale khác G).
/// </summary>
public static class AutoCaCorrect
{
    /// <summary>Ước lượng (Red, Blue) cho CaCorrectOp. (0,0) nếu không tìm thấy CA hoặc thiếu cạnh.</summary>
    public static (float Red, float Blue) Estimate(LinearImage img)
    {
        if (img == null) return (0f, 0f);
        int w = img.Width, h = img.Height;
        if (w < 16 || h < 16) return (0f, 0f);
        float[] px = img.Pixels;

        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float norm = 1f / MathF.Max(1f, MathF.Sqrt(cx * cx + cy * cy));

        // Thu thập pixel cạnh mạnh ở vùng ngoài (theo gradient G).
        var edges = new List<(int x, int y, float rad, float weight)>();
        // Tìm MAX gradient (ngưỡng theo max ổn định cho cả ảnh thưa cạnh lẫn texture dày).
        float maxMag = 0f;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float gx = G(px, w, x + 1, y) - G(px, w, x - 1, y);
                float gy = G(px, w, x, y + 1) - G(px, w, x, y - 1);
                float m = MathF.Sqrt(gx * gx + gy * gy);
                if (m > maxMag) maxMag = m;
            }
        if (maxMag < 1e-5f) return (0f, 0f);
        float thr = maxMag * 0.3f;

        for (int y = 2; y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
                float dx = x - cx, dy = y - cy;
                float rad = MathF.Sqrt(dx * dx + dy * dy) * norm;
                if (rad < 0.4f) continue; // CA rõ nhất ở mép
                float gx = G(px, w, x + 1, y) - G(px, w, x - 1, y);
                float gy = G(px, w, x, y + 1) - G(px, w, x, y - 1);
                float mag = MathF.Sqrt(gx * gx + gy * gy);
                if (mag < thr) continue;
                edges.Add((x, y, rad, mag));
            }
        if (edges.Count < 20) return (0f, 0f);

        float kr = SearchBestK(px, w, h, cx, cy, norm, edges, channelOffset: 0);
        float kb = SearchBestK(px, w, h, cx, cy, norm, edges, channelOffset: 2);

        // Red = -k/0.01 (đảo công thức op: kr_op = -Red*0.01).
        float red = -kr / 0.01f;
        float blue = -kb / 0.01f;
        return (Math.Clamp(red, -1f, 1f), Math.Clamp(blue, -1f, 1f));
    }

    // Tìm k tối thiểu hoá Σ w·(channel_resampled − G)² tại các pixel cạnh.
    private static float SearchBestK(float[] px, int w, int h, float cx, float cy, float norm,
        List<(int x, int y, float rad, float weight)> edges, int channelOffset)
    {
        // Quét thô [-0.02..0.02] (±2% bán kính ở mép), 41 bước; rồi tinh quanh đỉnh.
        float bestK = 0f; double bestErr = double.MaxValue;
        for (int i = -20; i <= 20; i++)
        {
            float k = i * 0.001f;
            double err = Cost(px, w, h, cx, cy, edges, channelOffset, k);
            if (err < bestErr) { bestErr = err; bestK = k; }
        }
        // Tinh chỉnh ±0.001 bước 0.0001.
        float coarse = bestK;
        for (int i = -10; i <= 10; i++)
        {
            float k = coarse + i * 0.0001f;
            double err = Cost(px, w, h, cx, cy, edges, channelOffset, k);
            if (err < bestErr) { bestErr = err; bestK = k; }
        }
        // Nếu k≈0 là tốt nhất -> không có CA đáng kể.
        if (MathF.Abs(bestK) < 0.0002f) return 0f;
        return bestK;
    }

    private static double Cost(float[] px, int w, int h, float cx, float cy,
        List<(int x, int y, float rad, float weight)> edges, int ch, float k)
    {
        double err = 0;
        foreach (var (x, y, rad, weight) in edges)
        {
            float dx = x - cx, dy = y - cy;
            float f = 1f + k * rad;
            float sx = cx + dx * f, sy = cy + dy * f;
            float c = SampleBilinear(px, w, h, sx, sy, ch);
            float g = px[(y * w + x) * 4 + 1];
            float d = c - g;
            err += weight * d * d;
        }
        return err;
    }

    private static float G(float[] px, int w, int x, int y) => px[(y * w + x) * 4 + 1];

    private static float SampleBilinear(float[] px, int w, int h, float sx, float sy, int ch)
    {
        if (sx < 0f) sx = 0f; else if (sx > w - 1) sx = w - 1;
        if (sy < 0f) sy = 0f; else if (sy > h - 1) sy = h - 1;
        int x0 = (int)sx, y0 = (int)sy;
        int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        float fx = sx - x0, fy = sy - y0;
        float v00 = px[(y0 * w + x0) * 4 + ch];
        float v10 = px[(y0 * w + x1) * 4 + ch];
        float v01 = px[(y1 * w + x0) * 4 + ch];
        float v11 = px[(y1 * w + x1) * 4 + ch];
        float top = v00 + (v10 - v00) * fx;
        float bot = v01 + (v11 - v01) * fx;
        return top + (bot - top) * fy;
    }
}
