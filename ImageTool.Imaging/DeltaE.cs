using System;

namespace ImageTool.Imaging;

/// <summary>
/// Đo độ lệch màu Delta-E (#9) — so sánh màu giữa 2 mẫu trong không gian Lab (D65). Hỗ trợ CIE76
/// (Euclid, nhanh) và CIEDE2000 (chuẩn công nghiệp, theo cảm nhận). Dùng để đánh giá độ chính xác màu
/// khi chụp bảng ColorChecker, hiệu chỉnh màn hình/profile. Thuần toán học -> unit test trực tiếp.
///
/// Quy ước Delta-E: &lt;1 mắt thường không phân biệt; 1-2 rất nhỏ; 2-10 nhận thấy; &gt;10 khác rõ.
/// </summary>
public static class DeltaE
{
    public readonly struct Lab
    {
        public readonly float L, A, B;
        public Lab(float l, float a, float b) { L = l; A = a; B = b; }
    }

    /// <summary>linear sRGB -> Lab (D65).</summary>
    public static Lab FromLinearRgb(float r, float g, float b)
    {
        float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
        float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
        float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;
        x /= 0.95047f; z /= 1.08883f;
        float fx = F(x), fy = F(y), fz = F(z);
        return new Lab(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    /// <summary>sRGB byte (0..255, gamma) -> Lab (D65).</summary>
    public static Lab FromSrgb8(int r, int g, int b)
        => FromLinearRgb(ColorSpace.DecodeByte((byte)Clamp255(r)),
                         ColorSpace.DecodeByte((byte)Clamp255(g)),
                         ColorSpace.DecodeByte((byte)Clamp255(b)));

    /// <summary>CIE76: khoảng cách Euclid trong Lab.</summary>
    public static float Cie76(Lab a, Lab b)
    {
        float dl = a.L - b.L, da = a.A - b.A, db = a.B - b.B;
        return MathF.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>CIEDE2000 — công thức Delta-E 2000 đầy đủ (kL=kC=kH=1).</summary>
    public static float Ciede2000(Lab s1, Lab s2)
    {
        double L1 = s1.L, a1 = s1.A, b1 = s1.B;
        double L2 = s2.L, a2 = s2.A, b2 = s2.B;

        double C1 = Math.Sqrt(a1 * a1 + b1 * b1);
        double C2 = Math.Sqrt(a2 * a2 + b2 * b2);
        double Cbar = (C1 + C2) / 2.0;

        double Cbar7 = Math.Pow(Cbar, 7);
        double G = 0.5 * (1 - Math.Sqrt(Cbar7 / (Cbar7 + Math.Pow(25, 7))));

        double a1p = (1 + G) * a1;
        double a2p = (1 + G) * a2;
        double C1p = Math.Sqrt(a1p * a1p + b1 * b1);
        double C2p = Math.Sqrt(a2p * a2p + b2 * b2);

        double h1p = HueDeg(b1, a1p);
        double h2p = HueDeg(b2, a2p);

        double dLp = L2 - L1;
        double dCp = C2p - C1p;

        double dhp;
        if (C1p * C2p == 0) dhp = 0;
        else
        {
            double diff = h2p - h1p;
            if (diff > 180) diff -= 360;
            else if (diff < -180) diff += 360;
            dhp = diff;
        }
        double dHp = 2 * Math.Sqrt(C1p * C2p) * Math.Sin(Deg2Rad(dhp / 2.0));

        double Lbarp = (L1 + L2) / 2.0;
        double Cbarp = (C1p + C2p) / 2.0;

        double hbarp;
        if (C1p * C2p == 0) hbarp = h1p + h2p;
        else
        {
            double diff = Math.Abs(h1p - h2p);
            double sum = h1p + h2p;
            if (diff <= 180) hbarp = sum / 2.0;
            else hbarp = sum < 360 ? (sum + 360) / 2.0 : (sum - 360) / 2.0;
        }

        double T = 1
            - 0.17 * Math.Cos(Deg2Rad(hbarp - 30))
            + 0.24 * Math.Cos(Deg2Rad(2 * hbarp))
            + 0.32 * Math.Cos(Deg2Rad(3 * hbarp + 6))
            - 0.20 * Math.Cos(Deg2Rad(4 * hbarp - 63));

        double dtheta = 30 * Math.Exp(-Math.Pow((hbarp - 275) / 25.0, 2));
        double Cbarp7 = Math.Pow(Cbarp, 7);
        double Rc = 2 * Math.Sqrt(Cbarp7 / (Cbarp7 + Math.Pow(25, 7)));
        double Sl = 1 + (0.015 * Math.Pow(Lbarp - 50, 2)) / Math.Sqrt(20 + Math.Pow(Lbarp - 50, 2));
        double Sc = 1 + 0.045 * Cbarp;
        double Sh = 1 + 0.015 * Cbarp * T;
        double Rt = -Math.Sin(Deg2Rad(2 * dtheta)) * Rc;

        double term1 = dLp / Sl;
        double term2 = dCp / Sc;
        double term3 = dHp / Sh;
        double de = Math.Sqrt(term1 * term1 + term2 * term2 + term3 * term3 + Rt * term2 * term3);
        return (float)de;
    }

    private static double HueDeg(double b, double ap)
    {
        if (b == 0 && ap == 0) return 0;
        double h = Math.Atan2(b, ap) * 180.0 / Math.PI;
        return h < 0 ? h + 360 : h;
    }
    private static double Deg2Rad(double d) => d * Math.PI / 180.0;

    private static float F(float t)
    {
        const float e = 0.008856f;
        return t > e ? MathF.Cbrt(t) : (7.787f * t + 16f / 116f);
    }
    private static int Clamp255(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
}
