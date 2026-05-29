using System;
using System.Numerics;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Op tổng hợp panel "Basic" kiểu Lightroom — GOM toàn bộ slider cơ bản vào 1 op duy nhất
/// (settings-bag). Lý do: trong LR/Darktable các chỉnh Basic là 1 trạng thái, không phải
/// chuỗi thao tác cộng dồn. Gộp 1 op giúp:
///   - Kéo slider = cập nhật tham số op hiện có (Upsert) rồi render lại — không phình history.
///   - Replay 1 lần, thứ tự xử lý cố định, không tích lũy sai số.
///
/// Toàn bộ tính trong LINEAR LIGHT. Thứ tự áp dụng (giống LR):
///   White Balance -> Exposure -> Tone regions (Highlights/Shadows/Whites/Blacks)
///   -> Contrast -> Vibrance -> Saturation.
/// </summary>
public sealed class DevelopBasicOp : IEditOp
{
    public const string Type = "DevelopBasic";
    public string OpType => Type;

    // Tham số chuẩn hoá [-1..1] trừ ghi chú khác. 0 = không đổi.
    public float Temp;        // ấm (+) / lạnh (-)
    public float Tint;        // tím (+) / xanh lá (-)
    public float Exposure;    // tính theo stops (EV), thường -5..+5
    public float Contrast;
    public float Highlights;
    public float Shadows;
    public float Whites;
    public float Blacks;
    public float Vibrance;
    public float Saturation;

    public static DevelopBasicOp FromParams(System.Collections.Generic.IReadOnlyDictionary<string, string> p)
        => new DevelopBasicOp
        {
            Temp = EditOpRegistry.F(p, "temp"),
            Tint = EditOpRegistry.F(p, "tint"),
            Exposure = EditOpRegistry.F(p, "exposure"),
            Contrast = EditOpRegistry.F(p, "contrast"),
            Highlights = EditOpRegistry.F(p, "highlights"),
            Shadows = EditOpRegistry.F(p, "shadows"),
            Whites = EditOpRegistry.F(p, "whites"),
            Blacks = EditOpRegistry.F(p, "blacks"),
            Vibrance = EditOpRegistry.F(p, "vibrance"),
            Saturation = EditOpRegistry.F(p, "saturation"),
        };

    public System.Collections.Generic.Dictionary<string, string> ToParams()
        => new()
        {
            ["temp"] = Fmt(Temp),
            ["tint"] = Fmt(Tint),
            ["exposure"] = Fmt(Exposure),
            ["contrast"] = Fmt(Contrast),
            ["highlights"] = Fmt(Highlights),
            ["shadows"] = Fmt(Shadows),
            ["whites"] = Fmt(Whites),
            ["blacks"] = Fmt(Blacks),
            ["vibrance"] = Fmt(Vibrance),
            ["saturation"] = Fmt(Saturation),
        };

    private static string Fmt(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True nếu tất cả tham số ~0 (op vô hại, có thể bỏ qua để khỏi phình history).</summary>
    public bool IsIdentity =>
        Near(Temp) && Near(Tint) && Near(Exposure) && Near(Contrast) &&
        Near(Highlights) && Near(Shadows) && Near(Whites) && Near(Blacks) &&
        Near(Vibrance) && Near(Saturation);

    private static bool Near(float v) => MathF.Abs(v) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        // --- Tiền tính các hệ số (ngoài vòng lặp pixel) ---
        float rGain = 1f + Math.Clamp(Temp, -1f, 1f) * 0.4f;
        float bGain = 1f - Math.Clamp(Temp, -1f, 1f) * 0.4f;
        float gGain = 1f + Math.Clamp(Tint, -1f, 1f) * 0.4f;
        bool doWb = !Near(Temp) || !Near(Tint);

        float expGain = MathF.Pow(2f, Exposure);
        bool doExp = !Near(Exposure);

        bool doTone = !Near(Highlights) || !Near(Shadows) || !Near(Whites) || !Near(Blacks);
        float hi = Math.Clamp(Highlights, -1f, 1f);
        float sh = Math.Clamp(Shadows, -1f, 1f);
        float wh = Math.Clamp(Whites, -1f, 1f);
        float bl = Math.Clamp(Blacks, -1f, 1f);

        const float pivot = 0.18f;
        float contrastF = 1f + Math.Clamp(Contrast, -1f, 1f);
        bool doContrast = !Near(Contrast);

        float satF = 1f + Math.Clamp(Saturation, -1f, 1f);
        bool doSat = !Near(Saturation);
        float vib = Math.Clamp(Vibrance, -1f, 1f);
        bool doVib = !Near(Vibrance);

        // SIMD fast-path: chỉ WB + Exposure (nhân per-channel, không có tone/contrast/sat/vib).
        // Đây là trường hợp phổ biến khi kéo Exposure/WB -> tăng tốc rõ rệt.
        if ((doWb || doExp) && !doTone && !doContrast && !doSat && !doVib)
        {
            float rg = (doWb ? rGain : 1f) * (doExp ? expGain : 1f);
            float gg2 = (doWb ? gGain : 1f) * (doExp ? expGain : 1f);
            float bg = (doWb ? bGain : 1f) * (doExp ? expGain : 1f);
            ApplyChannelGainSimd(image, rg, gg2, bg);
            return;
        }

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            // 1) White balance (channel gains)
            if (doWb) { r *= rGain; g *= gGain; b *= bGain; }

            // 2) Exposure (linear multiply)
            if (doExp) { r *= expGain; g *= expGain; b *= expGain; }

            // 3) Tone regions — thao tác trên độ sáng cảm nhận, bảo toàn tỉ lệ màu.
            if (doTone)
            {
                float lum = ColorSpace.Luminance(r, g, b);
                if (lum > 1e-6f)
                {
                    float p = ColorSpace.LinearToSrgb(lum); // vị trí cảm nhận [0..1]
                    float np = p;
                    // shadows: nâng/hạ vùng tối; highlights: phục hồi/đẩy vùng sáng.
                    float wSh = (1f - p); wSh *= wSh;          // mạnh ở tối
                    float wHi = p * p;                          // mạnh ở sáng
                    float wBl = wSh * (1f - p);                 // rất tối
                    float wWh = wHi * p;                        // rất sáng
                    np += sh * 0.5f * wSh;
                    np += hi * 0.5f * wHi;
                    np += bl * 0.5f * wBl;
                    np += wh * 0.5f * wWh;
                    if (np < 0f) np = 0f; else if (np > 1f) np = 1f;
                    float newLum = ColorSpace.SrgbToLinear(np);
                    float gain = newLum / lum;
                    r *= gain; g *= gain; b *= gain;
                }
            }

            // 4) Contrast quanh xám giữa (linear pivot 0.18)
            if (doContrast)
            {
                r = (r - pivot) * contrastF + pivot;
                g = (g - pivot) * contrastF + pivot;
                b = (b - pivot) * contrastF + pivot;
                if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
            }

            // 5) Vibrance — tăng bão hoà thông minh, ghìm pixel đã rực sẵn.
            if (doVib)
            {
                float lum = ColorSpace.Luminance(r, g, b);
                float mx = MathF.Max(r, MathF.Max(g, b));
                float mn = MathF.Min(r, MathF.Min(g, b));
                float sat = mx > 1e-6f ? (mx - mn) / mx : 0f;
                float amt = vib * (1f - sat);           // pixel càng rực càng ít tác động
                float f = 1f + amt;
                r = lum + (r - lum) * f;
                g = lum + (g - lum) * f;
                b = lum + (b - lum) * f;
                if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
            }

            // 6) Saturation tuyến tính
            if (doSat)
            {
                float lum = ColorSpace.Luminance(r, g, b);
                r = lum + (r - lum) * satF;
                g = lum + (g - lum) * satF;
                b = lum + (b - lum) * satF;
                if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
            }
        });
    }

    public static void Register(EditOpRegistry reg)
        => reg.Register(Type, FromParams);

    /// <summary>
    /// Nhân per-channel RGB (giữ alpha) bằng SIMD trên buffer phẳng RGBA. Pattern gain lặp
    /// [rg,gg,bg,1] khớp với layout R,G,B,A. Vector&lt;float&gt;.Count luôn là bội của 4
    /// (4/8/16) nên cửa sổ vector luôn căn theo ranh giới pixel.
    /// </summary>
    private static void ApplyChannelGainSimd(LinearImage image, float rg, float gg, float bg)
    {
        float[] px = image.Pixels;
        int len = px.Length;
        int simd = Vector<float>.Count;

        // Xây pattern gain dài simd theo chu kỳ 4.
        var pattern = new float[simd];
        for (int i = 0; i < simd; i++)
        {
            int ch = i & 3; // 0=R,1=G,2=B,3=A
            pattern[i] = ch switch { 0 => rg, 1 => gg, 2 => bg, _ => 1f };
        }
        var gainVec = new Vector<float>(pattern);

        // Vì simd là bội của 4, vector cửa sổ luôn bắt đầu ở kênh R -> pattern cố định.
        int vecEnd = len - (len % simd);
        Parallel.For(0, vecEnd / simd, k =>
        {
            int o = k * simd;
            var v = new Vector<float>(px, o);
            (v * gainVec).CopyTo(px, o);
        });

        // Phần dư cuối (nếu có), xử lý vô hướng theo kênh.
        for (int o = vecEnd; o < len; o++)
        {
            int ch = o & 3;
            if (ch == 0) px[o] *= rg;
            else if (ch == 1) px[o] *= gg;
            else if (ch == 2) px[o] *= bg;
        }
    }
}
