using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Gradient Map (#5): ánh xạ ĐỘ SÁNG (luminance) của mỗi pixel sang 1 dải màu gradient 3 chặng
/// (Shadow -> Mid -> Highlight), rồi blend với ảnh gốc theo Opacity. Hiệu ứng grading/cinematic,
/// kiểu Photoshop "Gradient Map" hoặc duotone/tritone.
///
/// Màu chặng nhập ở sRGB (0..1 mỗi kênh, dạng hex qua param), nội suy ở sRGB rồi đưa về linear để
/// blend. Mid point điều chỉnh được (vị trí 0..1 của chặng giữa). Thuần pixel-wise -> test trực tiếp.
/// </summary>
public sealed class GradientMapOp : IEditOp
{
    public const string Type = "GradientMap";
    public string OpType => Type;

    // Màu 3 chặng (sRGB 0..1).
    public float ShadowR, ShadowG, ShadowB;                 // mặc định đen
    public float MidR = 0.5f, MidG = 0.5f, MidB = 0.5f;     // mặc định xám
    public float HighR = 1f, HighG = 1f, HighB = 1f;        // mặc định trắng
    public float MidPoint = 0.5f;                            // vị trí chặng giữa (0..1)
    public float Opacity;                                    // 0 = tắt, 1 = thay hoàn toàn

    public bool IsIdentity => Opacity <= 0f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float op = Math.Clamp(Opacity, 0f, 1f);
        float mid = Math.Clamp(MidPoint, 0.02f, 0.98f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            // Luminance theo sRGB-encoded (cảm nhận) để map giống Photoshop.
            float sr = ColorSpace.LinearToSrgb(r);
            float sg = ColorSpace.LinearToSrgb(g);
            float sb = ColorSpace.LinearToSrgb(b);
            float l = 0.299f * sr + 0.587f * sg + 0.114f * sb;

            // Nội suy gradient 2 đoạn: [0..mid] shadow->mid, [mid..1] mid->high.
            float mr, mg, mb;
            if (l <= mid)
            {
                float t = l / mid;
                mr = Lerp(ShadowR, MidR, t); mg = Lerp(ShadowG, MidG, t); mb = Lerp(ShadowB, MidB, t);
            }
            else
            {
                float t = (l - mid) / (1f - mid);
                mr = Lerp(MidR, HighR, t); mg = Lerp(MidG, HighG, t); mb = Lerp(MidB, HighB, t);
            }

            // Đưa màu gradient về linear, blend theo opacity.
            float lr = ColorSpace.SrgbToLinear(mr);
            float lg = ColorSpace.SrgbToLinear(mg);
            float lb = ColorSpace.SrgbToLinear(mb);
            r = r + (lr - r) * op;
            g = g + (lg - g) * op;
            b = b + (lb - b) * op;
        });
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public Dictionary<string, string> ToParams() => new()
    {
        ["sh"] = Hex(ShadowR, ShadowG, ShadowB),
        ["mid"] = Hex(MidR, MidG, MidB),
        ["hi"] = Hex(HighR, HighG, HighB),
        ["midpoint"] = F(MidPoint),
        ["opacity"] = F(Opacity),
    };

    public static GradientMapOp FromParams(IReadOnlyDictionary<string, string> p)
    {
        var op = new GradientMapOp
        {
            MidPoint = EditOpRegistry.F(p, "midpoint", 0.5f),
            Opacity = EditOpRegistry.F(p, "opacity"),
        };
        (op.ShadowR, op.ShadowG, op.ShadowB) = ParseHex(EditOpRegistry.S(p, "sh", "000000"), 0, 0, 0);
        (op.MidR, op.MidG, op.MidB) = ParseHex(EditOpRegistry.S(p, "mid", "808080"), 0.5f, 0.5f, 0.5f);
        (op.HighR, op.HighG, op.HighB) = ParseHex(EditOpRegistry.S(p, "hi", "FFFFFF"), 1, 1, 1);
        return op;
    }

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Hex(float r, float g, float b)
        => $"{Byte(r):X2}{Byte(g):X2}{Byte(b):X2}";
    private static int Byte(float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static (float, float, float) ParseHex(string s, float dr, float dg, float db)
    {
        if (string.IsNullOrEmpty(s)) return (dr, dg, db);
        s = s.TrimStart('#');
        if (s.Length != 6 ||
            !int.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return (dr, dg, db);
        return (r / 255f, g / 255f, b / 255f);
    }
}
