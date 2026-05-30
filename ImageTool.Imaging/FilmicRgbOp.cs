using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Filmic RGB đầy đủ (D1.2, kiểu Darktable "filmic rgb") — tone mapping scene-referred có kiểm soát:
///   - WhiteRelative / BlackRelative: điểm trắng/đen tính theo EV quanh midgray (định nghĩa dải động).
///   - Latitude: vùng tuyến tính giữa (giữ tương phản trung gian), 0..1 theo % dải.
///   - Contrast: độ dốc đoạn giữa.
///   - Saturation: bù bão hoà vùng cực sáng/tối (filmic hay làm bạc màu highlight).
/// Map theo LUMINANCE (giữ hue), scale RGB theo gain. Amount blend với gốc.
///
/// Khác `FilmicOp` (ACES 1 nút): đây là đường cong cấu hình được, sát Darktable hơn.
/// </summary>
public sealed class FilmicRgbOp : IEditOp
{
    public const string Type = "FilmicRgb";
    public string OpType => Type;

    public float Amount;               // [0..1]
    public float WhiteRelative = 4f;   // EV trên midgray (highlight)
    public float BlackRelative = -6f;  // EV dưới midgray (shadow)
    public float Contrast = 1.2f;
    public float Latitude = 0.2f;      // [0..1] phần dải tuyến tính
    public float Saturation;           // [-1..1] bù sat vùng cực
    public float Pivot = 0.18f;        // midgray scene-linear

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float amt = Math.Clamp(Amount, 0f, 1f);
        float white = MathF.Max(0.5f, WhiteRelative);
        float black = MathF.Min(-0.5f, BlackRelative);
        float contrast = MathF.Max(0.1f, Contrast);
        float lat = Math.Clamp(Latitude, 0f, 0.95f);
        float pivot = Math.Clamp(Pivot, 0.01f, 0.9f);
        float satAdj = Math.Clamp(Saturation, -1f, 1f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float lum = ColorSpace.Luminance(r, g, b);
            if (lum <= 1e-6f) return;

            float mapped = FilmicCurve(lum, pivot, black, white, contrast, lat);
            float gain = mapped / lum;
            gain = 1f + (gain - 1f) * amt;

            float nr = r * gain, ng = g * gain, nb = b * gain;

            // Bù bão hoà ở vùng cực (mapped gần 0 hoặc 1): kéo về/ra xám theo satAdj.
            if (MathF.Abs(satAdj) > 1e-4f)
            {
                float extremity = MathF.Abs(2f * Math.Clamp(mapped, 0f, 1f) - 1f); // 0 giữa, 1 cực
                float f = 1f + satAdj * extremity * amt;
                float ml = ColorSpace.Luminance(nr, ng, nb);
                nr = ml + (nr - ml) * f;
                ng = ml + (ng - ml) * f;
                nb = ml + (nb - ml) * f;
            }

            r = nr < 0f ? 0f : nr;
            g = ng < 0f ? 0f : ng;
            b = nb < 0f ? 0f : nb;
        });
    }

    /// <summary>
    /// Đường cong filmic: chuyển scene-linear (theo EV quanh pivot) -> display [0..1].
    /// Vùng giữa (latitude) gần tuyến tính (theo contrast); 2 đuôi nén mượt về 0/1.
    /// </summary>
    private static float FilmicCurve(float x, float pivot, float blackEv, float whiteEv, float contrast, float latitude)
    {
        // vị trí theo EV, chuẩn hoá về [0..1] giữa black..white.
        float ev = MathF.Log2(MathF.Max(x, 1e-6f) / pivot);
        float t = (ev - blackEv) / (whiteEv - blackEv); // 0 ở black, 1 ở white
        t = Math.Clamp(t, 0f, 1f);

        // tâm latitude quanh 0.5; bên trong tuyến tính theo contrast, ngoài nén bằng smoothstep.
        float half = latitude * 0.5f;
        float lo = 0.5f - half, hi = 0.5f + half;
        float y;
        if (t < lo)
        {
            // đuôi tối: smoothstep từ 0 -> giá trị tại lo.
            float yl = Mid(lo, contrast);
            float u = lo > 0f ? t / lo : 0f;
            y = yl * (u * u * (3f - 2f * u));
        }
        else if (t > hi)
        {
            float yh = Mid(hi, contrast);
            float u = (1f - hi) > 0f ? (t - hi) / (1f - hi) : 0f;
            float s = u * u * (3f - 2f * u);
            y = yh + (1f - yh) * s;
        }
        else
        {
            y = Mid(t, contrast);
        }
        return Math.Clamp(y, 0f, 1f);
    }

    // đoạn giữa: tuyến tính quanh 0.5 với độ dốc = contrast.
    private static float Mid(float t, float contrast)
        => Math.Clamp(0.5f + (t - 0.5f) * contrast, 0f, 1f);

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["white"] = F(WhiteRelative), ["black"] = F(BlackRelative),
        ["contrast"] = F(Contrast), ["latitude"] = F(Latitude), ["sat"] = F(Saturation), ["pivot"] = F(Pivot),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static FilmicRgbOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Amount = EditOpRegistry.F(p, "amount"),
        WhiteRelative = EditOpRegistry.F(p, "white", 4f),
        BlackRelative = EditOpRegistry.F(p, "black", -6f),
        Contrast = EditOpRegistry.F(p, "contrast", 1.2f),
        Latitude = EditOpRegistry.F(p, "latitude", 0.2f),
        Saturation = EditOpRegistry.F(p, "sat"),
        Pivot = EditOpRegistry.F(p, "pivot", 0.18f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
