using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Hot/dead pixel removal (D3.3, kiểu Darktable "hot pixels"): tìm pixel có độ sáng lệch quá
/// xa so với 4 lân cận (trên/dưới/trái/phải) theo ngưỡng Threshold, thay bằng trung vị lân cận.
/// Chạy trên luminance để quyết định, thay cả 3 kênh bằng trung bình lân cận để giữ màu mượt.
/// Threshold [0..1]: nhỏ = bắt nhiều pixel hơn. Strength [0..1]: mức thay thế (blend).
/// </summary>
public sealed class HotPixelOp : IEditOp
{
    public const string Type = "HotPixel";
    public string OpType => Type;

    public float Threshold = 0.5f; // [0..1] ngưỡng lệch (1 = chỉ bắt pixel cực lệch)
    public float Strength = 1f;    // [0..1] mức thay thế

    public bool IsIdentity => Strength < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        if (w < 3 || h < 3) return;
        float[] src = image.Pixels;
        // ngưỡng tuyệt đối trên luminance: map [0..1] -> [0.02..0.8] (nhỏ -> nhạy).
        float thr = 0.02f + (1f - Math.Clamp(Threshold, 0f, 1f)) * 0.78f;
        float strength = Math.Clamp(Strength, 0f, 1f);

        // cần đọc từ bản gốc, ghi ra bản mới để không lan truyền.
        var dst = new float[src.Length];
        Array.Copy(src, dst, src.Length);

        Parallel.For(1, h - 1, y =>
        {
            int row = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                int c = (row + x) * 4;
                int up = ((y - 1) * w + x) * 4;
                int dn = ((y + 1) * w + x) * 4;
                int lf = (row + x - 1) * 4;
                int rt = (row + x + 1) * 4;

                float lc = ColorSpace.Luminance(src[c], src[c + 1], src[c + 2]);
                float lu = ColorSpace.Luminance(src[up], src[up + 1], src[up + 2]);
                float ld = ColorSpace.Luminance(src[dn], src[dn + 1], src[dn + 2]);
                float ll = ColorSpace.Luminance(src[lf], src[lf + 1], src[lf + 2]);
                float lr = ColorSpace.Luminance(src[rt], src[rt + 1], src[rt + 2]);

                // lân cận lớn nhất (loại chính nó). Pixel "nóng" vượt mọi lân cận quá thr,
                // "chết" thấp hơn mọi lân cận quá thr.
                float maxN = MathF.Max(MathF.Max(lu, ld), MathF.Max(ll, lr));
                float minN = MathF.Min(MathF.Min(lu, ld), MathF.Min(ll, lr));

                bool hot = lc - maxN > thr;
                bool dead = minN - lc > thr;
                if (!hot && !dead) continue;

                // thay bằng trung bình 4 lân cận, blend theo strength.
                for (int k = 0; k < 3; k++)
                {
                    float avg = (src[up + k] + src[dn + k] + src[lf + k] + src[rt + k]) * 0.25f;
                    dst[c + k] = src[c + k] + (avg - src[c + k]) * strength;
                }
            }
        });

        Array.Copy(dst, src, src.Length);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["threshold"] = F(Threshold), ["strength"] = F(Strength),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static HotPixelOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Threshold = EditOpRegistry.F(p, "threshold", 0.5f),
        Strength = EditOpRegistry.F(p, "strength", 1f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Lateral chromatic aberration correction (D3.4, kiểu Darktable "chromatic aberrations"): quang
/// sai màu trục làm kênh R và B phóng đại khác kênh G theo bán kính từ tâm ảnh. Sửa bằng cách
/// co/giãn kênh R và B quanh tâm với hệ số tỉ lệ theo bán kính (radial scale), lấy mẫu song tuyến.
/// Khác `DefringeOp` (chỉ khử viền màu cục bộ) — đây sửa lệch hình học theo bán kính.
///
/// Red/Blue [-1..1]: lượng co/giãn (đơn vị ~% ở mép). Dương = co kênh đó vào trong.
/// </summary>
public sealed class CaCorrectOp : IEditOp
{
    public const string Type = "CaCorrect";
    public string OpType => Type;

    public float Red;  // [-1..1]
    public float Blue; // [-1..1]

    public bool IsIdentity => MathF.Abs(Red) < 1e-4f && MathF.Abs(Blue) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] src = image.Pixels;
        var dst = new float[src.Length];
        Array.Copy(src, dst, src.Length);

        // hệ số dịch tỉ lệ ở mép (chuẩn hoá), độc lập kích thước/scale.
        // chia 100: Red=1 -> ~1% dịch ở mép.
        float kr = -Math.Clamp(Red, -1f, 1f) * 0.01f;
        float kb = -Math.Clamp(Blue, -1f, 1f) * 0.01f;

        float cx = (w - 1) * 0.5f;
        float cy = (h - 1) * 0.5f;
        float norm = 1f / MathF.Max(1f, MathF.Sqrt(cx * cx + cy * cy));

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float dx = x - cx, dy = y - cy;
                float rad = MathF.Sqrt(dx * dx + dy * dy) * norm; // 0 tâm -> 1 góc

                // R: lấy mẫu tại bán kính (1 + kr*rad) so với tâm.
                float fr = 1f + kr * rad;
                float fb = 1f + kb * rad;
                dst[p] = SampleBilinear(src, w, h, cx + dx * fr, cy + dy * fr, 0);
                dst[p + 2] = SampleBilinear(src, w, h, cx + dx * fb, cy + dy * fb, 2);
                // G và A giữ nguyên (đã copy).
            }
        });

        Array.Copy(dst, src, src.Length);
    }

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

    public Dictionary<string, string> ToParams() => new()
    {
        ["red"] = F(Red), ["blue"] = F(Blue),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static CaCorrectOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Red = EditOpRegistry.F(p, "red"),
        Blue = EditOpRegistry.F(p, "blue"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
