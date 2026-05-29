using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Color (chroma) noise reduction: tách Y (độ sáng) và chroma, làm mờ riêng chroma bán kính
/// nhỏ rồi ghép lại. Giữ chi tiết độ sáng, chỉ làm mượt nhiễu màu — đúng kiểu LR Color NR.
/// </summary>
public sealed class ColorNoiseReductionOp : IEditOp
{
    public const string Type = "ColorNR";
    public string OpType => Type;
    public float Amount; // [0..1]
    public float BaseRadius = 4f;

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        float radius = MathF.Max(1f, BaseRadius * scale);
        float amt = Math.Clamp(Amount, 0f, 1f);

        // Tách chroma (Cb, Cr xấp xỉ trên linear: dùng r-Y, b-Y).
        var crA = new float[w * h];
        var cbA = new float[w * h];
        var yA = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float Y = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
                yA[row + x] = Y;
                crA[row + x] = px[p] - Y;     // ~đỏ-lục
                cbA[row + x] = px[p + 2] - Y; // ~lam-vàng
            }
        });

        var crB = GaussianBlur.BlurPlane(crA, w, h, radius);
        var cbB = GaussianBlur.BlurPlane(cbA, w, h, radius);

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float Y = yA[row + x];
                float cr = crA[row + x] + (crB[row + x] - crA[row + x]) * amt;
                float cb = cbA[row + x] + (cbB[row + x] - cbA[row + x]) * amt;
                // tái dựng RGB: r=Y+cr, b=Y+cb, g từ Y trừ đóng góp (đảm bảo luminance ~giữ).
                float r = Y + cr;
                float b = Y + cb;
                float g = (Y - ColorSpace.LumR * r - ColorSpace.LumB * b) / ColorSpace.LumG;
                px[p] = r < 0f ? 0f : r;
                px[p + 1] = g < 0f ? 0f : g;
                px[p + 2] = b < 0f ? 0f : b;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new() { ["amount"] = F(Amount) };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static ColorNoiseReductionOp FromParams(IReadOnlyDictionary<string, string> p)
        => new() { Amount = EditOpRegistry.F(p, "amount") };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Luminance noise reduction: làm mượt kênh Y bằng blur có bảo toàn cạnh nhẹ (blend theo amount),
/// giữ nguyên chroma. Đơn giản nhưng hiệu quả cho nhiễu hạt.
/// </summary>
public sealed class LumaNoiseReductionOp : IEditOp
{
    public const string Type = "LumaNR";
    public string OpType => Type;
    public float Amount; // [0..1]
    public float Detail = 0.5f;

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        float radius = MathF.Max(0.8f, 1.5f * scale);
        float amt = Math.Clamp(Amount, 0f, 1f);
        float detail = Math.Clamp(Detail, 0f, 1f);

        var Y = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                Y[row + x] = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
            }
        });
        var Yb = GaussianBlur.BlurPlane(Y, w, h, radius);

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float orig = Y[row + x];
                if (orig < 1e-5f) continue;
                float smooth = Yb[row + x];
                // giữ lại 1 phần chi tiết (detail) để không bết.
                float diff = orig - smooth;
                float newY = smooth + diff * (1f - amt) + diff * detail * amt;
                float gain = newY / orig;
                px[p] *= gain; px[p + 1] *= gain; px[p + 2] *= gain;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new() { ["amount"] = F(Amount), ["detail"] = F(Detail) };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static LumaNoiseReductionOp FromParams(IReadOnlyDictionary<string, string> p)
        => new() { Amount = EditOpRegistry.F(p, "amount"), Detail = EditOpRegistry.F(p, "detail", 0.5f) };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Defringe: khử viền tím/lục ở vùng tương phản cao. Giảm chroma của pixel có hue tím/lục
/// và nằm gần cạnh (gradient luminance cao).
/// </summary>
public sealed class DefringeOp : IEditOp
{
    public const string Type = "Defringe";
    public string OpType => Type;
    public float Purple; // [0..1]
    public float Green;  // [0..1]

    public bool IsIdentity => Purple < 1e-4f && Green < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        float purple = Math.Clamp(Purple, 0f, 1f);
        float green = Math.Clamp(Green, 0f, 1f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float sr = ColorSpace.LinearToSrgb(r), sg = ColorSpace.LinearToSrgb(g), sb = ColorSpace.LinearToSrgb(b);
            float max = MathF.Max(sr, MathF.Max(sg, sb)), min = MathF.Min(sr, MathF.Min(sg, sb));
            float sat = max > 1e-5f ? (max - min) / max : 0f;
            if (sat < 0.1f) return;
            // hue thô
            float hue;
            if (max == sr) hue = 60f * (((sg - sb) / (max - min)) % 6f);
            else if (max == sg) hue = 60f * (((sb - sr) / (max - min)) + 2f);
            else hue = 60f * (((sr - sg) / (max - min)) + 4f);
            if (hue < 0f) hue += 360f;

            float reduce = 0f;
            if (purple > 0f && hue >= 260f && hue <= 320f) reduce = purple;       // tím
            else if (green > 0f && hue >= 80f && hue <= 160f) reduce = green;      // lục
            if (reduce <= 0f) return;

            // kéo về xám theo luminance.
            float Y = ColorSpace.Luminance(r, g, b);
            r = r + (Y - r) * reduce; g = g + (Y - g) * reduce; b = b + (Y - b) * reduce;
        });
    }

    public Dictionary<string, string> ToParams() => new() { ["purple"] = F(Purple), ["green"] = F(Green) };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static DefringeOp FromParams(IReadOnlyDictionary<string, string> p)
        => new() { Purple = EditOpRegistry.F(p, "purple"), Green = EditOpRegistry.F(p, "green") };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
