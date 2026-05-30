using System;

namespace ImageTool.Imaging;

/// <summary>
/// Chế độ blend (D4.5) — quyết định cách kết hợp pixel "edited" (kết quả op) với "base" (ảnh gốc)
/// trước khi nhân theo mask×opacity. Tương đương "blending" của Darktable / blend mode của Photoshop.
/// </summary>
public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    SoftLight,
    HardLight,
    Lighten,
    Darken,
    Addition,
    Subtract,
    Difference,
    LinearLight,
}

/// <summary>
/// Toán blend thuần (per-channel). Hoạt động trên giá trị sRGB [0..1] để khớp kỳ vọng người dùng
/// (giống Photoshop/Darktable định nghĩa blend trong display space). MaskedOp encode linear-&gt;sRGB,
/// blend, rồi decode về linear. Thuần hàm số -&gt; unit test trực tiếp.
/// </summary>
public static class BlendModes
{
    public static BlendMode Parse(string? s) => s?.ToLowerInvariant() switch
    {
        "multiply" => BlendMode.Multiply,
        "screen" => BlendMode.Screen,
        "overlay" => BlendMode.Overlay,
        "softlight" => BlendMode.SoftLight,
        "hardlight" => BlendMode.HardLight,
        "lighten" => BlendMode.Lighten,
        "darken" => BlendMode.Darken,
        "addition" or "add" => BlendMode.Addition,
        "subtract" => BlendMode.Subtract,
        "difference" => BlendMode.Difference,
        "linearlight" => BlendMode.LinearLight,
        _ => BlendMode.Normal,
    };

    public static string ToKey(BlendMode m) => m switch
    {
        BlendMode.Multiply => "multiply",
        BlendMode.Screen => "screen",
        BlendMode.Overlay => "overlay",
        BlendMode.SoftLight => "softlight",
        BlendMode.HardLight => "hardlight",
        BlendMode.Lighten => "lighten",
        BlendMode.Darken => "darken",
        BlendMode.Addition => "addition",
        BlendMode.Subtract => "subtract",
        BlendMode.Difference => "difference",
        BlendMode.LinearLight => "linearlight",
        _ => "normal",
    };

    /// <summary>Kết hợp 1 kênh: base (b) và lớp trên (t), cả hai trong [0..1]. Trả [0..1] (đã clamp).</summary>
    public static float Apply(BlendMode mode, float b, float t)
    {
        float r = mode switch
        {
            BlendMode.Normal => t,
            BlendMode.Multiply => b * t,
            BlendMode.Screen => 1f - (1f - b) * (1f - t),
            BlendMode.Overlay => b < 0.5f ? 2f * b * t : 1f - 2f * (1f - b) * (1f - t),
            BlendMode.HardLight => t < 0.5f ? 2f * b * t : 1f - 2f * (1f - b) * (1f - t),
            BlendMode.SoftLight => SoftLight(b, t),
            BlendMode.Lighten => MathF.Max(b, t),
            BlendMode.Darken => MathF.Min(b, t),
            BlendMode.Addition => b + t,
            BlendMode.Subtract => b - t,
            BlendMode.Difference => MathF.Abs(b - t),
            BlendMode.LinearLight => b + 2f * t - 1f,
            _ => t,
        };
        return r < 0f ? 0f : (r > 1f ? 1f : r);
    }

    // Soft light (công thức W3C/SVG).
    private static float SoftLight(float b, float t)
    {
        if (t <= 0.5f) return b - (1f - 2f * t) * b * (1f - b);
        float d = b <= 0.25f ? ((16f * b - 12f) * b + 4f) * b : MathF.Sqrt(b);
        return b + (2f * t - 1f) * (d - b);
    }
}
