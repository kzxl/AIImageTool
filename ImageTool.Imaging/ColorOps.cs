using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Split Toning (legacy LR): thêm sắc cho vùng sáng (Highlights) và vùng tối (Shadows) riêng,
/// có Balance điều khiển ranh giới. Tính trong linear, lấy sắc theo Hue+Sat.
/// </summary>
public sealed class SplitToningOp : IEditOp
{
    public const string Type = "SplitToning";
    public string OpType => Type;
    public float HiHue, HiSat, ShHue, ShSat, Balance;

    public bool IsIdentity => HiSat < 1e-4f && ShSat < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        var hi = HueToTint(HiHue, HiSat);
        var sh = HueToTint(ShHue, ShSat);
        float bal = Math.Clamp(Balance, -1f, 1f) * 0.5f + 0.5f; // 0..1, mặc định 0.5

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float L = ColorSpace.Luminance(r, g, b);
            float p = ColorSpace.LinearToSrgb(L);
            // trọng số highlight tăng theo p, qua điểm balance.
            float wHi = Smooth(p, bal - 0.25f, bal + 0.25f);
            float wSh = 1f - wHi;
            const float k = 0.3f;
            r += (hi.r * wHi * HiSat + sh.r * wSh * ShSat) * k;
            g += (hi.g * wHi * HiSat + sh.g * wSh * ShSat) * k;
            b += (hi.b * wHi * HiSat + sh.b * wSh * ShSat) * k;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    private static float Smooth(float x, float e0, float e1)
    {
        if (e1 <= e0) return x >= e1 ? 1f : 0f;
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static (float r, float g, float b) HueToTint(float hue, float sat)
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
        ["hiHue"] = F(HiHue), ["hiSat"] = F(HiSat), ["shHue"] = F(ShHue), ["shSat"] = F(ShSat), ["balance"] = F(Balance),
    };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static SplitToningOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        HiHue = EditOpRegistry.F(p, "hiHue"), HiSat = EditOpRegistry.F(p, "hiSat"),
        ShHue = EditOpRegistry.F(p, "shHue"), ShSat = EditOpRegistry.F(p, "shSat"),
        Balance = EditOpRegistry.F(p, "balance"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Channel Mixer / Calibration đơn giản: chỉnh hue+sat của 3 primary (R/G/B) — xoay nhẹ
/// sắc độ từng kênh quanh trục của nó. Gần với panel Calibration của Lightroom.
/// </summary>
public sealed class ChannelMixerOp : IEditOp
{
    public const string Type = "ChannelMixer";
    public string OpType => Type;
    // mỗi primary: hue shift [-1..1], sat [-1..1]
    public float RHue, RSat, GHue, GSat, BHue, BSat;

    public bool IsIdentity =>
        Near(RHue) && Near(RSat) && Near(GHue) && Near(GSat) && Near(BHue) && Near(BSat);
    private static bool Near(float v) => MathF.Abs(v) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        // Xấp xỉ: chuyển sang sat-weighted theo primary gần nhất, nudge hue.
        float rh = RHue * 20f, gh = GHue * 20f, bh = BHue * 20f;
        float rs = RSat, gs = GSat, bs = BSat;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float sr = ColorSpace.LinearToSrgb(r), sg = ColorSpace.LinearToSrgb(g), sb = ColorSpace.LinearToSrgb(b);
            RgbToHsv(sr, sg, sb, out float h, out float s, out float v);
            if (s < 1e-4f) return;
            // trọng số theo khoảng cách hue tới R(0)/G(120)/B(240).
            float wr = Tri(h, 0f), wg = Tri(h, 120f), wb = Tri(h, 240f);
            float sum = wr + wg + wb; if (sum < 1e-6f) return;
            wr /= sum; wg /= sum; wb /= sum;
            h += wr * rh + wg * gh + wb * bh;
            if (h < 0f) h += 360f; else if (h >= 360f) h -= 360f;
            float ds = wr * rs + wg * gs + wb * bs;
            s = Math.Clamp(s * (1f + ds), 0f, 1f);
            HsvToRgb(h, s, v, out sr, out sg, out sb);
            r = ColorSpace.SrgbToLinear(sr); g = ColorSpace.SrgbToLinear(sg); b = ColorSpace.SrgbToLinear(sb);
        });
    }

    private static float Tri(float hue, float center)
    {
        float d = MathF.Abs(hue - center); if (d > 180f) d = 360f - d;
        return d >= 120f ? 0f : (1f - d / 120f);
    }
    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        v = max; float d = max - min; s = max > 1e-6f ? d / max : 0f;
        if (d < 1e-6f) { h = 0f; return; }
        if (max == r) h = 60f * (((g - b) / d) % 6f);
        else if (max == g) h = 60f * (((b - r) / d) + 2f);
        else h = 60f * (((r - g) / d) + 4f);
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
        ["rHue"] = F(RHue), ["rSat"] = F(RSat), ["gHue"] = F(GHue), ["gSat"] = F(GSat), ["bHue"] = F(BHue), ["bSat"] = F(BSat),
    };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static ChannelMixerOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        RHue = EditOpRegistry.F(p, "rHue"), RSat = EditOpRegistry.F(p, "rSat"),
        GHue = EditOpRegistry.F(p, "gHue"), GSat = EditOpRegistry.F(p, "gSat"),
        BHue = EditOpRegistry.F(p, "bHue"), BSat = EditOpRegistry.F(p, "bSat"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
