using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Chuyển đen trắng (B&W) có kiểm soát (13.1). Khác desaturate đơn thuần: cho phép chỉnh
/// TRỌNG SỐ pha trộn từng kênh R/G/B khi quy về xám (như "B&W Mix" của Lightroom / channel mixer
/// mono của Photoshop) — ví dụ tăng Red làm da sáng hơn, tăng Blue làm trời tối hơn.
/// Sau đó tuỳ chọn nhuộm (split-tone đơn giản) theo ToneHue/ToneStrength.
///
/// Tính trên linear light: trọng số áp cho giá trị linear, chuẩn hoá để giữ độ sáng tổng thể.
/// </summary>
public sealed class BlackWhiteOp : IEditOp
{
    public const string Type = "BlackWhite";
    public string OpType => Type;

    public bool Enabled;            // false = không đổi (op vô hại)
    public float RedWeight = 0.299f;
    public float GreenWeight = 0.587f;
    public float BlueWeight = 0.114f;
    public float ToneHue;           // [0..360] màu nhuộm
    public float ToneStrength;      // [0..1] 0 = xám trung tính

    public bool IsIdentity => !Enabled;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        // Chuẩn hoá trọng số để tổng = 1 (giữ độ sáng), tránh chia 0.
        float sum = RedWeight + GreenWeight + BlueWeight;
        float wr, wg, wb;
        if (MathF.Abs(sum) < 1e-5f) { wr = 0.299f; wg = 0.587f; wb = 0.114f; }
        else { wr = RedWeight / sum; wg = GreenWeight / sum; wb = BlueWeight / sum; }

        float tStrength = Math.Clamp(ToneStrength, 0f, 1f);
        var tint = tStrength > 1e-4f ? HueToLinearRgb(ToneHue) : (0f, 0f, 0f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float gray = r * wr + g * wg + b * wb;
            if (gray < 0f) gray = 0f;
            if (tStrength > 1e-4f)
            {
                // nhuộm: nội suy giữa xám trung tính và xám * tint theo strength.
                r = gray * (1f - tStrength) + gray * tint.Item1 * 2f * tStrength;
                g = gray * (1f - tStrength) + gray * tint.Item2 * 2f * tStrength;
                b = gray * (1f - tStrength) + gray * tint.Item3 * 2f * tStrength;
            }
            else { r = gray; g = gray; b = gray; }
        });
    }

    /// <summary>Hue [0..360] -> linear RGB điểm bão hoà đầy (để nhuộm xám).</summary>
    private static (float, float, float) HueToLinearRgb(float hue)
    {
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
        return (ColorSpace.SrgbToLinear(r1), ColorSpace.SrgbToLinear(g1), ColorSpace.SrgbToLinear(b1));
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["enabled"] = Enabled ? "true" : "false",
        ["wr"] = F(RedWeight), ["wg"] = F(GreenWeight), ["wb"] = F(BlueWeight),
        ["toneHue"] = F(ToneHue), ["toneStr"] = F(ToneStrength),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static BlackWhiteOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Enabled = EditOpRegistry.B(p, "enabled"),
        RedWeight = EditOpRegistry.F(p, "wr", 0.299f),
        GreenWeight = EditOpRegistry.F(p, "wg", 0.587f),
        BlueWeight = EditOpRegistry.F(p, "wb", 0.114f),
        ToneHue = EditOpRegistry.F(p, "toneHue"),
        ToneStrength = EditOpRegistry.F(p, "toneStr"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
