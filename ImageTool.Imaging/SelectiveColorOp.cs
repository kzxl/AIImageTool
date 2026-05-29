using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Selective Color Grading (port từ ColorLab, non-destructive): dịch các pixel có hue gần
/// SourceHue (trong dải Tolerance) sang TargetHue, falloff mượt theo khoảng cách hue, bảo toàn
/// độ bão hoà & sáng. Thao tác qua HSV trên sRGB rồi đổi về linear.
/// </summary>
public sealed class SelectiveColorOp : IEditOp
{
    public const string Type = "SelectiveColor";
    public string OpType => Type;

    public float SourceHue;       // 0..360
    public float TargetHue;       // 0..360
    public float Tolerance = 30f; // độ, bán kính dải hue
    public float Strength = 1f;   // 0..1

    public bool IsIdentity => Strength < 1e-4f || Tolerance < 1e-3f ||
                              MathF.Abs(HueDiff(SourceHue, TargetHue)) < 1e-3f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float src = Norm(SourceHue), tgt = Norm(TargetHue);
        float tol = MathF.Max(1f, Tolerance);
        float strength = Math.Clamp(Strength, 0f, 1f);
        float shift = HueDiff(src, tgt); // độ cần dịch (-180..180)

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float sr = ColorSpace.LinearToSrgb(r), sg = ColorSpace.LinearToSrgb(g), sb = ColorSpace.LinearToSrgb(b);
            RgbToHsv(sr, sg, sb, out float h, out float s, out float v);
            if (s < 0.05f) return; // gần xám: bỏ qua
            float d = MathF.Abs(HueDiff(h, src));
            if (d > tol) return;
            float falloff = 0.5f * (1f + MathF.Cos(MathF.PI * d / tol)); // 1 ở tâm -> 0 ở mép
            float applied = strength * falloff;
            h = Norm(h + shift * applied);
            HsvToRgb(h, s, v, out sr, out sg, out sb);
            r = ColorSpace.SrgbToLinear(sr); g = ColorSpace.SrgbToLinear(sg); b = ColorSpace.SrgbToLinear(sb);
        });
    }

    private static float Norm(float h) { h %= 360f; if (h < 0f) h += 360f; return h; }
    private static float HueDiff(float a, float b)
    {
        float d = Norm(a) - Norm(b);
        if (d > 180f) d -= 360f; else if (d < -180f) d += 360f;
        return d;
    }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        v = max; float dd = max - min; s = max > 1e-6f ? dd / max : 0f;
        if (dd < 1e-6f) { h = 0f; return; }
        if (max == r) h = 60f * (((g - b) / dd) % 6f);
        else if (max == g) h = 60f * (((b - r) / dd) + 2f);
        else h = 60f * (((r - g) / dd) + 4f);
        if (h < 0f) h += 360f;
    }
    private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        float c = v * s, x = c * (1f - MathF.Abs(((h / 60f) % 2f) - 1f)), m = v - c;
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
        ["src"] = F(SourceHue), ["tgt"] = F(TargetHue), ["tol"] = F(Tolerance), ["strength"] = F(Strength),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static SelectiveColorOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        SourceHue = EditOpRegistry.F(p, "src"), TargetHue = EditOpRegistry.F(p, "tgt"),
        Tolerance = EditOpRegistry.F(p, "tol", 30f), Strength = EditOpRegistry.F(p, "strength", 1f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
