using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// White balance theo nhiệt độ Kelvin thật + tint. Chuyển Kelvin -> điểm trắng (xấp xỉ
/// Planckian/Tanner Helland), tính gain kênh tương đối so với điểm trắng tham chiếu (mặc định
/// 6500K = D65). Tint dịch trục lục-tím. Khác WB gain đơn giản trong DevelopBasic ở chỗ thang
/// đo là Kelvin trực quan (2000..12000) như Lightroom.
/// </summary>
public sealed class WhiteBalanceKelvinOp : IEditOp
{
    public const string Type = "WBKelvin";
    public string OpType => Type;

    public float Kelvin = 6500f; // nhiệt độ mong muốn
    public float Tint;           // [-1..1] lục(-)..tím(+)
    public float RefKelvin = 6500f;

    public bool IsIdentity => MathF.Abs(Kelvin - RefKelvin) < 1f && MathF.Abs(Tint) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        // Gain = white(ref) / white(target). Khi Kelvin cao (xanh hơn) -> bù bằng cách tăng đỏ.
        var wTarget = KelvinToLinearRgb(Math.Clamp(Kelvin, 1500f, 15000f));
        var wRef = KelvinToLinearRgb(Math.Clamp(RefKelvin, 1500f, 15000f));
        float rGain = SafeDiv(wRef.r, wTarget.r);
        float gGain = SafeDiv(wRef.g, wTarget.g);
        float bGain = SafeDiv(wRef.b, wTarget.b);
        // chuẩn hoá theo G để giữ độ sáng tương đối.
        float norm = gGain > 1e-6f ? 1f / gGain : 1f;
        rGain *= norm; gGain *= norm; bGain *= norm;
        // tint: dịch lục/tím (giảm/tăng G).
        float t = Math.Clamp(Tint, -1f, 1f);
        gGain *= 1f - t * 0.2f;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r *= rGain; g *= gGain; b *= bGain;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    private static float SafeDiv(float a, float b) => b > 1e-6f ? a / b : 1f;

    /// <summary>Kelvin -> linear RGB điểm trắng (Tanner Helland approx, rồi sRGB->linear).</summary>
    private static (float r, float g, float b) KelvinToLinearRgb(float kelvin)
    {
        float temp = kelvin / 100f;
        float r, g, b;
        // Red
        if (temp <= 66f) r = 255f;
        else { r = temp - 60f; r = 329.698727446f * MathF.Pow(r, -0.1332047592f); r = Clamp255(r); }
        // Green
        if (temp <= 66f) { g = temp; g = 99.4708025861f * MathF.Log(g) - 161.1195681661f; }
        else { g = temp - 60f; g = 288.1221695283f * MathF.Pow(g, -0.0755148492f); }
        g = Clamp255(g);
        // Blue
        if (temp >= 66f) b = 255f;
        else if (temp <= 19f) b = 0f;
        else { b = temp - 10f; b = 138.5177312231f * MathF.Log(b) - 305.0447927307f; b = Clamp255(b); }

        return (ColorSpace.SrgbToLinear(r / 255f), ColorSpace.SrgbToLinear(g / 255f), ColorSpace.SrgbToLinear(b / 255f));
    }
    private static float Clamp255(float v) => v < 0f ? 0f : (v > 255f ? 255f : v);

    public Dictionary<string, string> ToParams() => new()
    {
        ["kelvin"] = F(Kelvin), ["tint"] = F(Tint), ["ref"] = F(RefKelvin),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static WhiteBalanceKelvinOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Kelvin = EditOpRegistry.F(p, "kelvin", 6500f),
        Tint = EditOpRegistry.F(p, "tint"),
        RefKelvin = EditOpRegistry.F(p, "ref", 6500f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
