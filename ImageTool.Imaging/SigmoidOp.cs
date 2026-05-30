using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Sigmoid tone mapping (D1.1, kiểu Darktable "sigmoid"): nén dải động scene-linear về display bằng
/// đường cong sigmoid trơn, ít vỡ màu ở vùng rực hơn ACES/filmic đơn giản. Áp trên LUMINANCE rồi
/// scale RGB theo cùng tỉ lệ (per-channel mode tuỳ chọn) để giữ sắc.
///
/// Tham số:
///  - Contrast: độ dốc đường cong (1 = chuẩn, &gt;1 tương phản hơn).
///  - Skew/Pivot: điểm giữa xám (0.18 mặc định).
///  - Amount: mức blend với ảnh gốc [0..1].
///  - PerChannel: true = áp sigmoid từng kênh (giống film), false = theo luminance giữ hue.
/// </summary>
public sealed class SigmoidOp : IEditOp
{
    public const string Type = "Sigmoid";
    public string OpType => Type;

    public float Amount;             // [0..1]
    public float Contrast = 1.5f;    // độ dốc
    public float Pivot = 0.18f;      // midgray scene-linear
    public bool PerChannel;

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float amt = Math.Clamp(Amount, 0f, 1f);
        float contrast = MathF.Max(0.1f, Contrast);
        float pivot = Math.Clamp(Pivot, 0.01f, 0.9f);
        bool perCh = PerChannel;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            if (perCh)
            {
                r = r + (Sigmoid(r, contrast, pivot) - r) * amt;
                g = g + (Sigmoid(g, contrast, pivot) - g) * amt;
                b = b + (Sigmoid(b, contrast, pivot) - b) * amt;
            }
            else
            {
                float lum = ColorSpace.Luminance(r, g, b);
                if (lum <= 1e-6f) return;
                float mapped = Sigmoid(lum, contrast, pivot);
                float gain = mapped / lum;
                gain = 1f + (gain - 1f) * amt;
                r *= gain; g *= gain; b *= gain;
            }
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }

    /// <summary>
    /// Sigmoid display transform: chuyển scene-linear x về [0..1) trơn. Dùng dạng x/(x+k) chuẩn hoá
    /// quanh pivot, với contrast điều khiển độ cong. Đảm bảo đơn điệu tăng.
    /// </summary>
    private static float Sigmoid(float x, float contrast, float pivot)
    {
        if (x <= 0f) return 0f;
        // log-space quanh pivot rồi qua hàm logistic, chuẩn hoá để pivot -> 0.5 ở display.
        float lx = MathF.Log2(MathF.Max(x, 1e-6f) / pivot);   // 0 tại pivot
        float s = 1f / (1f + MathF.Exp(-contrast * lx));       // logistic [0..1]
        return s;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["contrast"] = F(Contrast), ["pivot"] = F(Pivot),
        ["perChannel"] = PerChannel ? "true" : "false",
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static SigmoidOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Amount = EditOpRegistry.F(p, "amount"),
        Contrast = EditOpRegistry.F(p, "contrast", 1.5f),
        Pivot = EditOpRegistry.F(p, "pivot", 0.18f),
        PerChannel = EditOpRegistry.B(p, "perChannel"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
