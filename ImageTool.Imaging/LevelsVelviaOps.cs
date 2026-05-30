using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// RGB Levels (D2.5) — điểm đen / xám (gamma) / trắng như Levels của Photoshop/Darktable.
/// Black/White [0..1] (sRGB) định nghĩa input range; Gamma điều chỉnh midtone. Áp trên sRGB rồi về linear.
/// </summary>
public sealed class RgbLevelsOp : IEditOp
{
    public const string Type = "RgbLevels";
    public string OpType => Type;

    public float Black;       // [0..1) điểm đen input
    public float White = 1f;  // (0..1] điểm trắng input
    public float Gamma = 1f;  // (0.1..10) midtone

    public bool IsIdentity =>
        MathF.Abs(Black) < 1e-4f && MathF.Abs(White - 1f) < 1e-4f && MathF.Abs(Gamma - 1f) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float bp = Math.Clamp(Black, 0f, 0.98f);
        float wp = Math.Clamp(White, bp + 0.01f, 1f);
        float invGamma = 1f / Math.Clamp(Gamma, 0.1f, 10f);
        float range = wp - bp;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r = Map(r, bp, range, invGamma);
            g = Map(g, bp, range, invGamma);
            b = Map(b, bp, range, invGamma);
        });
    }

    private static float Map(float lin, float bp, float range, float invGamma)
    {
        float s = ColorSpace.LinearToSrgb(lin);
        float t = (s - bp) / range;
        if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
        t = MathF.Pow(t, invGamma);
        return ColorSpace.SrgbToLinear(t);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["black"] = F(Black), ["white"] = F(White), ["gamma"] = F(Gamma),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static RgbLevelsOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Black = EditOpRegistry.F(p, "black"),
        White = EditOpRegistry.F(p, "white", 1f),
        Gamma = EditOpRegistry.F(p, "gamma", 1f),
    };
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
