using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

/// <summary>
/// HSL / Color Mixer kiểu Lightroom — 8 dải màu (Red, Orange, Yellow, Green, Aqua, Blue,
/// Purple, Magenta), mỗi dải chỉnh Hue / Saturation / Luminance độc lập.
///
/// Hoạt động: với mỗi pixel, tính hue (HSV) trong sRGB-perceptual, xác định trọng số thuộc
/// về từng dải (cửa sổ mượt quanh tâm dải, có chồng lấn), rồi nội suy 3 chỉnh số theo trọng số.
/// Phép biến đổi thực hiện trên linear light để giữ chất lượng (chuyển linear->sRGB lấy hue,
/// chỉnh trong HSV, rồi đổi ngược về linear).
/// </summary>
public sealed class HslMixerOp : IEditOp
{
    public const string Type = "HslMixer";
    public string OpType => Type;

    public const int Bands = 8;
    // Tâm hue (độ) của 8 dải, khớp Lightroom.
    private static readonly float[] BandCenters = { 0f, 30f, 60f, 120f, 180f, 240f, 280f, 320f };
    public static readonly string[] BandNames = { "red", "orange", "yellow", "green", "aqua", "blue", "purple", "magenta" };

    // Mỗi mảng dài 8, giá trị [-1..1]. 0 = không đổi.
    public float[] Hue = new float[Bands];
    public float[] Sat = new float[Bands];
    public float[] Lum = new float[Bands];

    public bool IsIdentity
    {
        get
        {
            for (int i = 0; i < Bands; i++)
                if (MathF.Abs(Hue[i]) > 1e-4f || MathF.Abs(Sat[i]) > 1e-4f || MathF.Abs(Lum[i]) > 1e-4f)
                    return false;
            return true;
        }
    }

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float[] hueAdj = Hue, satAdj = Sat, lumAdj = Lum;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            // linear -> sRGB để thao tác theo cảm nhận màu.
            float sr = ColorSpace.LinearToSrgb(r);
            float sg = ColorSpace.LinearToSrgb(g);
            float sb = ColorSpace.LinearToSrgb(b);

            RgbToHsv(sr, sg, sb, out float h, out float s, out float v);
            if (s < 1e-4f) return; // pixel xám: không thuộc dải màu nào

            // Trọng số thuộc về từng dải (tổng có thể != 1, chuẩn hoá sau).
            float dH = 0f, dS = 0f, dL = 0f, wSum = 0f;
            for (int i = 0; i < Bands; i++)
            {
                float w = BandWeight(h, BandCenters[i]);
                if (w <= 0f) continue;
                wSum += w;
                dH += w * hueAdj[i];
                dS += w * satAdj[i];
                dL += w * lumAdj[i];
            }
            if (wSum > 1e-6f)
            {
                float inv = 1f / wSum;
                dH *= inv; dS *= inv; dL *= inv;
            }

            // Áp chỉnh: hue dịch tối đa ±30°, sat nhân, lum nhân nhẹ trên V.
            h += dH * 30f;
            if (h < 0f) h += 360f; else if (h >= 360f) h -= 360f;
            s = Math.Clamp(s * (1f + dS), 0f, 1f);
            v = Math.Clamp(v * (1f + dL * 0.5f), 0f, 1f);

            HsvToRgb(h, s, v, out sr, out sg, out sb);
            r = ColorSpace.SrgbToLinear(sr);
            g = ColorSpace.SrgbToLinear(sg);
            b = ColorSpace.SrgbToLinear(sb);
        });
    }

    /// <summary>Trọng số mượt (cosine) của hue quanh tâm dải; cửa sổ ±45° để các dải chồng nhẹ.</summary>
    private static float BandWeight(float hue, float center)
    {
        float d = MathF.Abs(hue - center);
        if (d > 180f) d = 360f - d;
        const float window = 45f;
        if (d >= window) return 0f;
        // cos falloff: 1 ở tâm, 0 ở mép.
        return 0.5f * (1f + MathF.Cos(MathF.PI * d / window));
    }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        v = max;
        float delta = max - min;
        s = max > 1e-6f ? delta / max : 0f;
        if (delta < 1e-6f) { h = 0f; return; }
        if (max == r) h = 60f * (((g - b) / delta) % 6f);
        else if (max == g) h = 60f * (((b - r) / delta) + 2f);
        else h = 60f * (((r - g) / delta) + 4f);
        if (h < 0f) h += 360f;
    }

    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs(((h / 60f) % 2f) - 1f));
        float m = v - c;
        float r1, g1, b1;
        if (h < 60f) { r1 = c; g1 = x; b1 = 0f; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0f; }
        else if (h < 180f) { r1 = 0f; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0f; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0f; b1 = c; }
        else { r1 = c; g1 = 0f; b1 = x; }
        r = r1 + m; g = g1 + m; b = b1 + m;
    }

    public Dictionary<string, string> ToParams()
    {
        var p = new Dictionary<string, string>();
        for (int i = 0; i < Bands; i++)
        {
            p[$"h_{BandNames[i]}"] = Fmt(Hue[i]);
            p[$"s_{BandNames[i]}"] = Fmt(Sat[i]);
            p[$"l_{BandNames[i]}"] = Fmt(Lum[i]);
        }
        return p;
    }

    public static HslMixerOp FromParams(IReadOnlyDictionary<string, string> p)
    {
        var op = new HslMixerOp();
        for (int i = 0; i < Bands; i++)
        {
            op.Hue[i] = EditOpRegistry.F(p, $"h_{BandNames[i]}");
            op.Sat[i] = EditOpRegistry.F(p, $"s_{BandNames[i]}");
            op.Lum[i] = EditOpRegistry.F(p, $"l_{BandNames[i]}");
        }
        return op;
    }

    private static string Fmt(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
