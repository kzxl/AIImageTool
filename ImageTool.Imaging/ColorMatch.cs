using System;

namespace ImageTool.Imaging;

/// <summary>
/// Color Match / Color Transfer (#8) — "mượn" tông màu từ 1 ảnh THAM CHIẾU sang ảnh ĐÍCH bằng
/// Reinhard transfer trong không gian Lab: dịch + co giãn mỗi kênh (L, a, b) sao cho mean/std của
/// ảnh đích khớp ảnh tham chiếu. Có Strength để pha trộn (0 = giữ nguyên, 1 = khớp hoàn toàn).
///
/// Thuần toán học (Lab D65) -> unit test trực tiếp. Dùng cho grading: làm 1 loạt ảnh đồng tông màu.
/// </summary>
public static class ColorMatch
{
    private readonly struct LabStats
    {
        public readonly float ML, Ma, Mb;     // mean
        public readonly float SL, Sa, Sb;     // std
        public LabStats(float ml, float ma, float mb, float sl, float sa, float sb)
        { ML = ml; Ma = ma; Mb = mb; SL = sl; Sa = sa; Sb = sb; }
    }

    /// <summary>Thống kê Lab (mean+std mỗi kênh) của 1 ảnh — đủ để tái lập color transfer (lưu vào op).</summary>
    public readonly struct Stats
    {
        public readonly float ML, Ma, Mb, SL, Sa, Sb;
        public Stats(float ml, float ma, float mb, float sl, float sa, float sb)
        { ML = ml; Ma = ma; Mb = mb; SL = sl; Sa = sa; Sb = sb; }
    }

    /// <summary>Tính thống kê Lab của ảnh tham chiếu (để lưu vào ColorMatchOp, replay không cần ảnh gốc).</summary>
    public static Stats Measure(LinearImage img)
    {
        var s = ComputeStats(img);
        return new Stats(s.ML, s.Ma, s.Mb, s.SL, s.Sa, s.Sb);
    }

    /// <summary>
    /// Áp tông màu của <paramref name="reference"/> lên <paramref name="target"/> (sửa tại chỗ target).
    /// <paramref name="strength"/> 0..1. An toàn nếu std ảnh tham chiếu ~0 (giữ nguyên kênh đó).
    /// </summary>
    public static void Apply(LinearImage target, LinearImage reference, float strength = 1f)
    {
        if (target == null || reference == null) return;
        float s = Math.Clamp(strength, 0f, 1f);
        if (s <= 0f) return;

        LabStats tgt = ComputeStats(target);
        LabStats refS = ComputeStats(reference);

        // Hệ số co giãn theo tỉ lệ std (kẹp để tránh khuếch đại nhiễu quá mức).
        float kL = Ratio(refS.SL, tgt.SL);
        float ka = Ratio(refS.Sa, tgt.Sa);
        float kb = Ratio(refS.Sb, tgt.Sb);

        var px = target.Pixels;
        int n = target.PixelCount;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            RgbToLab(px[o], px[o + 1], px[o + 2], out float L, out float a, out float b);

            // Reinhard: (v - mean_tgt) * k + mean_ref
            float nL = (L - tgt.ML) * kL + refS.ML;
            float na = (a - tgt.Ma) * ka + refS.Ma;
            float nb = (b - tgt.Mb) * kb + refS.Mb;

            // Pha trộn theo strength.
            nL = L + (nL - L) * s;
            na = a + (na - a) * s;
            nb = b + (nb - b) * s;

            LabToRgb(nL, na, nb, out float r, out float g, out float bb);
            px[o] = r < 0f ? 0f : r;
            px[o + 1] = g < 0f ? 0f : g;
            px[o + 2] = bb < 0f ? 0f : bb;
        }
    }

    private static float Ratio(float refStd, float tgtStd)
    {
        if (tgtStd < 1e-4f) return 1f;             // ảnh đích phẳng -> không co giãn.
        float k = refStd / tgtStd;
        return Math.Clamp(k, 0.25f, 4f);           // chống khuếch đại/nén cực đoan.
    }

    /// <summary>
    /// Áp color transfer dùng THỐNG KÊ tham chiếu đã đo sẵn (ColorMatchOp replay). Tính stats ảnh đích
    /// tại chỗ rồi Reinhard về thống kê tham chiếu, pha theo strength.
    /// </summary>
    public static void ApplyStats(LinearImage target, Stats refS, float strength)
    {
        if (target == null) return;
        float s = Math.Clamp(strength, 0f, 1f);
        if (s <= 0f) return;
        LabStats tgt = ComputeStats(target);
        float kL = Ratio(refS.SL, tgt.SL);
        float ka = Ratio(refS.Sa, tgt.Sa);
        float kb = Ratio(refS.Sb, tgt.Sb);
        var px = target.Pixels;
        int n = target.PixelCount;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            RgbToLab(px[o], px[o + 1], px[o + 2], out float L, out float a, out float b);
            float nL = (L - tgt.ML) * kL + refS.ML;
            float na = (a - tgt.Ma) * ka + refS.Ma;
            float nb = (b - tgt.Mb) * kb + refS.Mb;
            nL = L + (nL - L) * s; na = a + (na - a) * s; nb = b + (nb - b) * s;
            LabToRgb(nL, na, nb, out float r, out float g, out float bb);
            px[o] = r < 0f ? 0f : r;
            px[o + 1] = g < 0f ? 0f : g;
            px[o + 2] = bb < 0f ? 0f : bb;
        }
    }

    private static LabStats ComputeStats(LinearImage img)
    {
        var px = img.Pixels;
        int n = img.PixelCount;
        double sL = 0, sa = 0, sb = 0;
        // Pass 1: mean.
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            RgbToLab(px[o], px[o + 1], px[o + 2], out float L, out float a, out float b);
            sL += L; sa += a; sb += b;
        }
        float mL = (float)(sL / n), ma = (float)(sa / n), mb = (float)(sb / n);
        // Pass 2: variance.
        double vL = 0, va = 0, vb = 0;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            RgbToLab(px[o], px[o + 1], px[o + 2], out float L, out float a, out float b);
            double dL = L - mL, da = a - ma, db = b - mb;
            vL += dL * dL; va += da * da; vb += db * db;
        }
        return new LabStats(mL, ma, mb,
            (float)Math.Sqrt(vL / n), (float)Math.Sqrt(va / n), (float)Math.Sqrt(vb / n));
    }

    // --- Lab (D65) <-> linear sRGB (cùng công thức ColorContrastOp) ---
    private static void RgbToLab(float r, float g, float b, out float L, out float a, out float bb)
    {
        float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
        float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
        float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;
        x /= 0.95047f; z /= 1.08883f;
        float fx = LabF(x), fy = LabF(y), fz = LabF(z);
        L = 116f * fy - 16f;
        a = 500f * (fx - fy);
        bb = 200f * (fy - fz);
    }

    private static void LabToRgb(float L, float a, float bb, out float r, out float g, out float b)
    {
        float fy = (L + 16f) / 116f;
        float fx = fy + a / 500f;
        float fz = fy - bb / 200f;
        float x = LabFInv(fx) * 0.95047f;
        float y = LabFInv(fy);
        float z = LabFInv(fz) * 1.08883f;
        r = x * 3.2404542f + y * -1.5371385f + z * -0.4985314f;
        g = x * -0.9692660f + y * 1.8760108f + z * 0.0415560f;
        b = x * 0.0556434f + y * -0.2040259f + z * 1.0572252f;
    }

    private static float LabF(float t)
    {
        const float e = 0.008856f;
        return t > e ? MathF.Cbrt(t) : (7.787f * t + 16f / 116f);
    }
    private static float LabFInv(float t)
    {
        float t3 = t * t * t;
        return t3 > 0.008856f ? t3 : (t - 16f / 116f) / 7.787f;
    }
}
