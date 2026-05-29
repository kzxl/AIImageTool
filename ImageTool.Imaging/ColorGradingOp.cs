using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

/// <summary>
/// Color Grading 3-way kiểu Lightroom: chỉnh tông màu riêng cho Shadows / Midtones /
/// Highlights + Global. Mỗi vùng có Hue (0..360), Sat (0..1), Lum (-1..1).
/// Trọng số vùng theo luminance cảm nhận (sRGB). Áp thêm màu (additive trong linear,
/// theo sắc độ chọn) có trọng số mượt giữa các vùng.
/// </summary>
public sealed class ColorGradingOp : IEditOp
{
    public const string Type = "ColorGrading";
    public string OpType => Type;

    // 4 vùng: 0=Shadows, 1=Midtones, 2=Highlights, 3=Global
    public float[] Hue = new float[4];
    public float[] Sat = new float[4];
    public float[] Lum = new float[4];
    public float Blending = 0.5f; // độ chồng lấn giữa các vùng

    public bool IsIdentity
    {
        get
        {
            for (int i = 0; i < 4; i++)
                if (Sat[i] > 1e-4f || MathF.Abs(Lum[i]) > 1e-4f) return false;
            return true;
        }
    }

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;

        // Tiền tính màu (linear RGB) cho từng vùng từ Hue+Sat.
        var tint = new (float r, float g, float b)[4];
        for (int i = 0; i < 4; i++)
            tint[i] = HsToLinearRgb(Hue[i], Sat[i]);
        float[] lum = Lum;
        float blend = Math.Clamp(Blending, 0f, 1f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float L = ColorSpace.Luminance(r, g, b);
            float p = ColorSpace.LinearToSrgb(L); // vị trí tông [0..1]

            // Trọng số 3 vùng (mượt). width điều khiển bởi blend.
            float wHi = Smooth(p, 0.5f + 0.2f * (1f - blend), 1f);
            float wSh = Smooth(1f - p, 0.5f + 0.2f * (1f - blend), 1f);
            float wMid = MathF.Max(0f, 1f - wHi - wSh);

            // Cộng màu (shadows/midtones/highlights) + global.
            float addR = tint[0].r * wSh * Sat[0] + tint[1].r * wMid * Sat[1] + tint[2].r * wHi * Sat[2] + tint[3].r * Sat[3];
            float addG = tint[0].g * wSh * Sat[0] + tint[1].g * wMid * Sat[1] + tint[2].g * wHi * Sat[2] + tint[3].g * Sat[3];
            float addB = tint[0].b * wSh * Sat[0] + tint[1].b * wMid * Sat[1] + tint[2].b * wHi * Sat[2] + tint[3].b * Sat[3];
            const float strength = 0.25f;
            r += addR * strength; g += addG * strength; b += addB * strength;

            // Luminance shift theo vùng.
            float dL = lum[0] * wSh + lum[1] * wMid + lum[2] * wHi + lum[3];
            if (MathF.Abs(dL) > 1e-5f)
            {
                float f = 1f + dL * 0.3f;
                r *= f; g *= f; b *= f;
            }

            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    // smoothstep giữa edge0..edge1
    private static float Smooth(float x, float edge0, float edge1)
    {
        if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static (float r, float g, float b) HsToLinearRgb(float hue, float sat)
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
        // sang linear, trừ midgray để là "thêm sắc" (centered).
        return (ColorSpace.SrgbToLinear(r1) - 0.5f, ColorSpace.SrgbToLinear(g1) - 0.5f, ColorSpace.SrgbToLinear(b1) - 0.5f);
    }

    public Dictionary<string, string> ToParams()
    {
        var p = new Dictionary<string, string>();
        string[] z = { "sh", "mid", "hi", "glob" };
        for (int i = 0; i < 4; i++)
        {
            p[$"h_{z[i]}"] = F(Hue[i]);
            p[$"s_{z[i]}"] = F(Sat[i]);
            p[$"l_{z[i]}"] = F(Lum[i]);
        }
        p["blend"] = F(Blending);
        return p;
    }

    public static ColorGradingOp FromParams(IReadOnlyDictionary<string, string> p)
    {
        var op = new ColorGradingOp { Blending = EditOpRegistry.F(p, "blend", 0.5f) };
        string[] z = { "sh", "mid", "hi", "glob" };
        for (int i = 0; i < 4; i++)
        {
            op.Hue[i] = EditOpRegistry.F(p, $"h_{z[i]}");
            op.Sat[i] = EditOpRegistry.F(p, $"s_{z[i]}");
            op.Lum[i] = EditOpRegistry.F(p, $"l_{z[i]}");
        }
        return op;
    }

    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
