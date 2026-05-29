using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Color Unification (port phi phá hủy của ColorLab cũ): kéo nhẹ sắc độ (hue) của ảnh về
/// 1 màu đích, đồng thời hoà bớt độ bão hoà về phía đích. Giữ luminance (L trong HSL) nguyên
/// để không đổi độ sáng — chỉ thống nhất "tông màu" toàn ảnh (kiểu teal&amp;orange, sepia...).
///
/// Khác bản ColorLab gốc: chạy trên linear light (chuyển linear-&gt;sRGB lấy HSL, dịch hue/sat,
/// rồi đổi ngược) nên replay được, không tích luỹ sai số, và khớp proxy/full-res.
///
/// Tham số: TargetHue (0..360), TargetSat (0..1), Intensity (0..1). Pixel gần như xám
/// (S &lt; ngưỡng) không bị dịch để tránh nhuộm màu nền trung tính.
/// </summary>
public sealed class ColorUnifyOp : IEditOp
{
    public const string Type = "ColorUnify";
    public string OpType => Type;

    public float TargetHue;          // độ [0..360]
    public float TargetSat = 0.5f;   // [0..1]
    public float Intensity;          // [0..1] 0 = không đổi

    public bool IsIdentity => MathF.Abs(Intensity) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float tH = ((TargetHue % 360f) + 360f) % 360f;
        float tS = Math.Clamp(TargetSat, 0f, 1f);
        float k = Math.Clamp(Intensity, 0f, 1f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float sr = ColorSpace.LinearToSrgb(r), sg = ColorSpace.LinearToSrgb(g), sb = ColorSpace.LinearToSrgb(b);
            RgbToHsl(sr, sg, sb, out float h, out float s, out float l);
            if (s < 0.05f) return; // pixel gần xám: bỏ qua

            // Khoảng cách hue ngắn nhất trên vòng tròn màu.
            float diff = tH - h;
            if (diff > 180f) diff -= 360f;
            else if (diff < -180f) diff += 360f;

            float newH = h + diff * k;
            if (newH < 0f) newH += 360f; else if (newH >= 360f) newH -= 360f;

            // Hoà saturation nhẹ về phía đích (một nửa cường độ).
            float newS = s + (tS - s) * (k * 0.5f);

            HslToRgb(newH, Math.Clamp(newS, 0f, 1f), l, out sr, out sg, out sb);
            r = ColorSpace.SrgbToLinear(sr);
            g = ColorSpace.SrgbToLinear(sg);
            b = ColorSpace.SrgbToLinear(sb);
        });
    }

    private static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        l = (max + min) * 0.5f;
        float d = max - min;
        if (d < 1e-6f) { h = 0f; s = 0f; return; }
        s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        if (max == r) h = 60f * (((g - b) / d) % 6f);
        else if (max == g) h = 60f * (((b - r) / d) + 2f);
        else h = 60f * (((r - g) / d) + 4f);
        if (h < 0f) h += 360f;
    }

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        if (s < 1e-6f) { r = g = b = l; return; }
        float c = (1f - MathF.Abs(2f * l - 1f)) * s;
        float x = c * (1f - MathF.Abs(((h / 60f) % 2f) - 1f));
        float m = l - c * 0.5f;
        float r1, g1, b1;
        if (h < 60f) { r1 = c; g1 = x; b1 = 0f; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0f; }
        else if (h < 180f) { r1 = 0f; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0f; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0f; b1 = c; }
        else { r1 = c; g1 = 0f; b1 = x; }
        r = r1 + m; g = g1 + m; b = b1 + m;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["hue"] = F(TargetHue), ["sat"] = F(TargetSat), ["intensity"] = F(Intensity),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static ColorUnifyOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        TargetHue = EditOpRegistry.F(p, "hue"),
        TargetSat = EditOpRegistry.F(p, "sat", 0.5f),
        Intensity = EditOpRegistry.F(p, "intensity"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
