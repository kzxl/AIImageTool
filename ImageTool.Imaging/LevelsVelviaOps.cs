using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// RGB Levels (D2.5) — điểm đen / xám (gamma) / trắng như Levels của Photoshop/Darktable.
/// Black/White [0..1] (sRGB) định nghĩa input range; Gamma điều chỉnh midtone. Áp trên sRGB rồi về linear.
///
/// Hỗ trợ PER-CHANNEL: ngoài kênh tổng (master) còn có black/white/gamma riêng cho R/G/B (mặc định
/// trùng master), cho phép color grading kiểu film (vd nâng black kênh Blue). Áp master rồi per-channel.
/// </summary>
public sealed class RgbLevelsOp : IEditOp
{
    public const string Type = "RgbLevels";
    public string OpType => Type;

    public float Black;       // [0..1) điểm đen input (master)
    public float White = 1f;  // (0..1] điểm trắng input (master)
    public float Gamma = 1f;  // (0.1..10) midtone (master)

    // Per-channel (NaN = "kế thừa master"). Cho phép chỉnh từng kênh R/G/B độc lập.
    public float BlackR = float.NaN, WhiteR = float.NaN, GammaR = float.NaN;
    public float BlackG = float.NaN, WhiteG = float.NaN, GammaG = float.NaN;
    public float BlackB = float.NaN, WhiteB = float.NaN, GammaB = float.NaN;

    private bool MasterIdentity =>
        MathF.Abs(Black) < 1e-4f && MathF.Abs(White - 1f) < 1e-4f && MathF.Abs(Gamma - 1f) < 1e-4f;

    private static bool ChannelIdentity(float b, float w, float g)
        => MathF.Abs(b) < 1e-4f && MathF.Abs(w - 1f) < 1e-4f && MathF.Abs(g - 1f) < 1e-4f;

    public bool IsIdentity
    {
        get
        {
            // Hiệu lực mỗi kênh = per-channel nếu có, ngược lại master.
            float br = Eff(BlackR, Black), wr = Eff(WhiteR, White), gr = Eff(GammaR, Gamma);
            float bg = Eff(BlackG, Black), wg = Eff(WhiteG, White), gg = Eff(GammaG, Gamma);
            float bb = Eff(BlackB, Black), wb = Eff(WhiteB, White), gb = Eff(GammaB, Gamma);
            return ChannelIdentity(br, wr, gr) && ChannelIdentity(bg, wg, gg) && ChannelIdentity(bb, wb, gb);
        }
    }

    private static float Eff(float channel, float master) => float.IsNaN(channel) ? master : channel;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;

        PrepChannel(Eff(BlackR, Black), Eff(WhiteR, White), Eff(GammaR, Gamma), out float bpR, out float rangeR, out float igR);
        PrepChannel(Eff(BlackG, Black), Eff(WhiteG, White), Eff(GammaG, Gamma), out float bpG, out float rangeG, out float igG);
        PrepChannel(Eff(BlackB, Black), Eff(WhiteB, White), Eff(GammaB, Gamma), out float bpB, out float rangeB, out float igB);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r = Map(r, bpR, rangeR, igR);
            g = Map(g, bpG, rangeG, igG);
            b = Map(b, bpB, rangeB, igB);
        });
    }

    private static void PrepChannel(float black, float white, float gamma, out float bp, out float range, out float invGamma)
    {
        bp = Math.Clamp(black, 0f, 0.98f);
        float wp = Math.Clamp(white, bp + 0.01f, 1f);
        range = wp - bp;
        invGamma = 1f / Math.Clamp(gamma, 0.1f, 10f);
    }

    private static float Map(float lin, float bp, float range, float invGamma)
    {
        float s = ColorSpace.LinearToSrgb(lin);
        float t = (s - bp) / range;
        if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
        t = MathF.Pow(t, invGamma);
        return ColorSpace.SrgbToLinear(t);
    }

    public Dictionary<string, string> ToParams()
    {
        var d = new Dictionary<string, string>
        {
            ["black"] = F(Black), ["white"] = F(White), ["gamma"] = F(Gamma),
        };
        // Chỉ ghi per-channel khi có (khác NaN) để giữ params gọn + tương thích cũ.
        AddIf(d, "blackR", BlackR); AddIf(d, "whiteR", WhiteR); AddIf(d, "gammaR", GammaR);
        AddIf(d, "blackG", BlackG); AddIf(d, "whiteG", WhiteG); AddIf(d, "gammaG", GammaG);
        AddIf(d, "blackB", BlackB); AddIf(d, "whiteB", WhiteB); AddIf(d, "gammaB", GammaB);
        return d;
    }

    private static void AddIf(Dictionary<string, string> d, string key, float v)
    {
        if (!float.IsNaN(v)) d[key] = F(v);
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static RgbLevelsOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Black = EditOpRegistry.F(p, "black"),
        White = EditOpRegistry.F(p, "white", 1f),
        Gamma = EditOpRegistry.F(p, "gamma", 1f),
        BlackR = Opt(p, "blackR"), WhiteR = Opt(p, "whiteR"), GammaR = Opt(p, "gammaR"),
        BlackG = Opt(p, "blackG"), WhiteG = Opt(p, "whiteG"), GammaG = Opt(p, "gammaG"),
        BlackB = Opt(p, "blackB"), WhiteB = Opt(p, "whiteB"), GammaB = Opt(p, "gammaB"),
    };

    private static float Opt(IReadOnlyDictionary<string, string> p, string key)
        => p.ContainsKey(key) ? EditOpRegistry.F(p, key) : float.NaN;

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Velvia (D2.3) — tăng bão hoà thông minh kiểu film Velvia: vùng ít rực được tăng nhiều, vùng đã
/// rực tăng ít (tránh vỡ), có ghìm theo độ sáng để không làm bẩn vùng tối/sáng. Amount [0..1].
/// </summary>
public sealed class VelviaOp : IEditOp
{
    public const string Type = "Velvia";
    public string OpType => Type;
    public float Amount; // [0..1]

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float amt = Math.Clamp(Amount, 0f, 1f);
        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float lum = ColorSpace.Luminance(r, g, b);
            float mx = MathF.Max(r, MathF.Max(g, b));
            float mn = MathF.Min(r, MathF.Min(g, b));
            float sat = mx > 1e-6f ? (mx - mn) / mx : 0f;
            // tăng nhiều ở sat thấp, ít ở sat cao; ghìm ở rất tối/sáng.
            float lumW = 1f - MathF.Abs(2f * Math.Clamp(ColorSpace.LinearToSrgb(lum), 0f, 1f) - 1f);
            float f = 1f + amt * (1f - sat) * lumW * 1.2f;
            r = lum + (r - lum) * f;
            g = lum + (g - lum) * f;
            b = lum + (b - lum) * f;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    public Dictionary<string, string> ToParams() => new() { ["amount"] = F(Amount) };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static VelviaOp FromParams(IReadOnlyDictionary<string, string> p)
        => new() { Amount = EditOpRegistry.F(p, "amount") };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
