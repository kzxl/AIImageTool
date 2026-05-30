using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Color Contrast (D2.4, kiểu Darktable "color contrast") — tăng/giảm tương phản trên 2 trục đối màu
/// trong Lab: a* (green↔magenta) và b* (blue↔yellow). Đẩy a*/b* ra xa 0 -> màu "căng" hơn;
/// kéo về 0 -> nhạt hơn. Giữ L* (độ sáng) nguyên.
/// </summary>
public sealed class ColorContrastOp : IEditOp
{
    public const string Type = "ColorContrast";
    public string OpType => Type;

    public float GreenMagenta; // [-1..1] hệ số trục a*
    public float BlueYellow;   // [-1..1] hệ số trục b*

    public bool IsIdentity => MathF.Abs(GreenMagenta) < 1e-4f && MathF.Abs(BlueYellow) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float fa = 1f + Math.Clamp(GreenMagenta, -1f, 1f);
        float fb = 1f + Math.Clamp(BlueYellow, -1f, 1f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            RgbToLab(r, g, b, out float L, out float aa, out float bb);
            aa *= fa;
            bb *= fb;
            LabToRgb(L, aa, bb, out r, out g, out b);
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    // --- Lab conversion (D65), input/output linear RGB ---
    private static void RgbToLab(float r, float g, float b, out float L, out float a, out float bb)
    {
        // linear sRGB -> XYZ (D65)
        float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
        float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
        float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;
        // chuẩn hoá theo white point D65
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
        // XYZ -> linear sRGB
        r = x * 3.2404542f + y * -1.5371385f + z * -0.4985314f;
        g = x * -0.9692660f + y * 1.8760108f + z * 0.0415560f;
        b = x * 0.0556434f + y * -0.2040259f + z * 1.0572252f;
    }

    private static float LabF(float t)
    {
        const float d = 6f / 29f;
        return t > d * d * d ? MathF.Cbrt(t) : t / (3f * d * d) + 4f / 29f;
    }
    private static float LabFInv(float t)
    {
        const float d = 6f / 29f;
        return t > d ? t * t * t : 3f * d * d * (t - 4f / 29f);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["greenMagenta"] = F(GreenMagenta), ["blueYellow"] = F(BlueYellow),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static ColorContrastOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        GreenMagenta = EditOpRegistry.F(p, "greenMagenta"),
        BlueYellow = EditOpRegistry.F(p, "blueYellow"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
