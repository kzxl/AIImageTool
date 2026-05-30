using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Color Balance RGB 4-way (D2.1, kiểu Darktable "color balance rgb" / lift-gamma-gain).
/// 3 tầng tác động theo tông:
///   - Lift (shadows): cộng offset, mạnh ở vùng tối.
///   - Gamma (midtones): luỹ thừa, tác động trung gian.
///   - Gain (highlights): nhân, mạnh ở vùng sáng.
/// Mỗi tầng có sắc (hue+sat -> vector màu) áp theo trọng số tông. + Global chroma/contrast.
/// Áp trên linear; sắc lấy từ HSL chuyển sang offset RGB.
/// </summary>
public sealed class ColorBalanceRgbOp : IEditOp
{
    public const string Type = "ColorBalanceRgb";
    public string OpType => Type;

    // mỗi tầng: hue (0..360) + sat (0..1) + luminance shift (-1..1)
    public float LiftHue, LiftSat, LiftLum;
    public float GammaHue, GammaSat, GammaLum;
    public float GainHue, GainSat, GainLum;
    public float GlobalChroma;   // [-1..1] tăng/giảm bão hoà tổng
    public float GlobalContrast; // [-1..1]

    public bool IsIdentity =>
        Z(LiftSat) && Z(LiftLum) && Z(GammaSat) && Z(GammaLum) && Z(GainSat) && Z(GainLum)
        && Z(GlobalChroma) && Z(GlobalContrast);
    private static bool Z(float v) => MathF.Abs(v) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        var lift = Tint(LiftHue, LiftSat);
        var gamma = Tint(GammaHue, GammaSat);
        var gain = Tint(GainHue, GainSat);
        float liftLum = LiftLum, gammaLum = GammaLum, gainLum = GainLum;
        float chroma = Math.Clamp(GlobalChroma, -1f, 1f);
        float contrast = Math.Clamp(GlobalContrast, -1f, 1f);
        const float pivot = 0.18f;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float lum = ColorSpace.Luminance(r, g, b);
            float p = ColorSpace.LinearToSrgb(lum); // vị trí tông [0..1]

            // trọng số 3 tầng theo tông.
            float wSh = (1f - p); wSh *= wSh;       // tối
            float wHi = p * p;                       // sáng
            float wMid = MathF.Max(0f, 1f - wSh - wHi);

            // Lift (offset, vùng tối) + Gain (nhân, vùng sáng) + Gamma (luỹ thừa, giữa) — qua màu.
            r += lift.r * wSh * LiftSat * 0.2f + gain.r * wHi * GainSat * 0.2f + gamma.r * wMid * GammaSat * 0.2f;
            g += lift.g * wSh * LiftSat * 0.2f + gain.g * wHi * GainSat * 0.2f + gamma.g * wMid * GammaSat * 0.2f;
            b += lift.b * wSh * LiftSat * 0.2f + gain.b * wHi * GainSat * 0.2f + gamma.b * wMid * GammaSat * 0.2f;

            // luminance shift theo tầng.
            float dL = liftLum * wSh + gammaLum * wMid + gainLum * wHi;
            if (MathF.Abs(dL) > 1e-5f)
            {
                float f = 1f + dL * 0.3f;
                r *= f; g *= f; b *= f;
            }

            // global contrast quanh pivot.
            if (MathF.Abs(contrast) > 1e-5f)
            {
                float cf = 1f + contrast;
                r = (r - pivot) * cf + pivot;
                g = (g - pivot) * cf + pivot;
                b = (b - pivot) * cf + pivot;
            }

            // global chroma (bão hoà).
            if (MathF.Abs(chroma) > 1e-5f)
            {
                float ml = ColorSpace.Luminance(r, g, b);
                float f = 1f + chroma;
                r = ml + (r - ml) * f;
                g = ml + (g - ml) * f;
                b = ml + (b - ml) * f;
            }

            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    private static (float r, float g, float b) Tint(float hue, float sat)
    {
        if (sat < 1e-5f) return (0f, 0f, 0f);
        float h = ((hue % 360f) + 360f) % 360f;
        float c = 1f;
        float x = c * (1f - MathF.Abs(((h / 60f) % 2f) - 1f));
        float r1, g1, b1;
        if (h < 60f) { r1 = c; g1 = x; b1 = 0f; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0f; }
        else if (h < 180f) { r1 = 0f; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0f; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0f; b1 = c; }
        else { r1 = c; g1 = 0f; b1 = x; }
        return (ColorSpace.SrgbToLinear(r1) - 0.5f, ColorSpace.SrgbToLinear(g1) - 0.5f, ColorSpace.SrgbToLinear(b1) - 0.5f);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["liftHue"] = F(LiftHue), ["liftSat"] = F(LiftSat), ["liftLum"] = F(LiftLum),
        ["gammaHue"] = F(GammaHue), ["gammaSat"] = F(GammaSat), ["gammaLum"] = F(GammaLum),
        ["gainHue"] = F(GainHue), ["gainSat"] = F(GainSat), ["gainLum"] = F(GainLum),
        ["chroma"] = F(GlobalChroma), ["contrast"] = F(GlobalContrast),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static ColorBalanceRgbOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        LiftHue = EditOpRegistry.F(p, "liftHue"), LiftSat = EditOpRegistry.F(p, "liftSat"), LiftLum = EditOpRegistry.F(p, "liftLum"),
        GammaHue = EditOpRegistry.F(p, "gammaHue"), GammaSat = EditOpRegistry.F(p, "gammaSat"), GammaLum = EditOpRegistry.F(p, "gammaLum"),
        GainHue = EditOpRegistry.F(p, "gainHue"), GainSat = EditOpRegistry.F(p, "gainSat"), GainLum = EditOpRegistry.F(p, "gainLum"),
        GlobalChroma = EditOpRegistry.F(p, "chroma"), GlobalContrast = EditOpRegistry.F(p, "contrast"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
