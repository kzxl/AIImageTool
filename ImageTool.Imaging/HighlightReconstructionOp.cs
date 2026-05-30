using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Highlight Reconstruction (D5.3, kiểu Darktable "highlight reconstruction"): khi 1-2 kênh bị cháy
/// (clip gần/quá 1.0) nhưng kênh khác chưa, vùng sáng thường bị ám màu sai (vd hồng/lục ở mây, da).
/// Op này phục hồi bằng cách KÉO các kênh đã clip về phía trung tính (theo kênh chưa clip) ở vùng
/// rất sáng — giảm ám màu, trả lại highlight "trắng" tự nhiên.
///
/// Hoạt động trên ảnh thường (không chỉ RAW): với pixel có max-channel &gt;= Threshold, trộn màu về
/// xám-trắng theo Amount, mức trộn tỉ lệ độ "cháy". Threshold [0..1], Amount [0..1].
/// </summary>
public sealed class HighlightReconstructionOp : IEditOp
{
    public const string Type = "HighlightRecon";
    public string OpType => Type;

    public float Amount;            // [0..1]
    public float Threshold = 0.85f; // [0..1] bắt đầu phục hồi từ độ sáng này

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float amt = Math.Clamp(Amount, 0f, 1f);
        // ngưỡng trong linear (Threshold cho ở thang sRGB-perceptual cho trực giác).
        float thrLin = ColorSpace.SrgbToLinear(Math.Clamp(Threshold, 0f, 1f));
        float soft = MathF.Max(1e-3f, (1f - thrLin) * 0.6f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float max = MathF.Max(r, MathF.Max(g, b));
            if (max < thrLin) return;

            // mức "cháy": 0 tại thr, 1 khi max >= 1.
            float t = Smoothstep(thrLin, thrLin + soft, max);
            float w = t * amt;
            if (w <= 0f) return;

            // mục tiêu trung tính = giá trị sáng nhất (giữ độ sáng, bỏ ám màu).
            float target = max;
            r = r + (target - r) * w;
            g = g + (target - g) * w;
            b = b + (target - b) * w;
        });
    }

    private static float Smoothstep(float e0, float e1, float x)
    {
        if (e1 <= e0) return x >= e1 ? 1f : 0f;
        float tt = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return tt * tt * (3f - 2f * tt);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["threshold"] = F(Threshold),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static HighlightReconstructionOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Amount = EditOpRegistry.F(p, "amount"),
        Threshold = EditOpRegistry.F(p, "threshold", 0.85f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
