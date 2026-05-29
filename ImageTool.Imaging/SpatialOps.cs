using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Clarity: tăng tương phản cục bộ vùng trung gian (midtone local contrast) bằng cách
/// đẩy luminance ra xa bản blur bán kính lớn. Bảo vệ vùng quá sáng/tối để tránh quầng.
/// Bán kính nhân theo scale để preview proxy khớp full-res.
/// </summary>
public sealed class ClarityOp : IEditOp
{
    public const string Type = "Clarity";
    public string OpType => Type;
    public float Amount; // [-1..1]
    public float BaseRadius = 40f; // px ở full-res

    public bool IsIdentity => MathF.Abs(Amount) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float radius = MathF.Max(1f, BaseRadius * scale);
        var blur = GaussianBlur.BlurLuminance(image, radius);
        float[] px = image.Pixels;
        float amt = Math.Clamp(Amount, -1f, 1f) * 0.8f;

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float lum = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
                if (lum < 1e-5f) continue;
                float local = lum - blur[row + x];      // high-pass
                // ghìm ở rất sáng / rất tối để tránh halo.
                float prot = 1f - MathF.Abs(2f * MathF.Min(1f, lum) - 1f);
                float newLum = lum + local * amt * prot;
                if (newLum < 0f) newLum = 0f;
                float gain = newLum / lum;
                px[p] *= gain; px[p + 1] *= gain; px[p + 2] *= gain;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = Amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    };
    public static ClarityOp FromParams(IReadOnlyDictionary<string, string> p)
        => new() { Amount = EditOpRegistry.F(p, "amount") };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Texture: tăng/giảm chi tiết tần số trung-cao (high-pass bán kính nhỏ). Khác Clarity ở
/// bán kính nhỏ hơn nhiều -> tác động lên vân/chi tiết nhỏ thay vì khối lớn.
/// </summary>
public sealed class TextureOp : IEditOp
{
    public const string Type = "Texture";
    public string OpType => Type;
    public float Amount; // [-1..1]
    public float BaseRadius = 3f;

    public bool IsIdentity => MathF.Abs(Amount) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float radius = MathF.Max(0.6f, BaseRadius * scale);
        var blur = GaussianBlur.BlurLuminance(image, radius);
        float[] px = image.Pixels;
        float amt = Math.Clamp(Amount, -1f, 1f);

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float lum = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
                if (lum < 1e-5f) continue;
                float detail = lum - blur[row + x];
                float newLum = lum + detail * amt;
                if (newLum < 0f) newLum = 0f;
                float gain = newLum / lum;
                px[p] *= gain; px[p + 1] *= gain; px[p + 2] *= gain;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = Amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
    };
    public static TextureOp FromParams(IReadOnlyDictionary<string, string> p)
        => new() { Amount = EditOpRegistry.F(p, "amount") };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Sharpening kiểu unsharp mask: detail = lum - blur(bán kính nhỏ); lum += amount*detail.
/// Có ngưỡng (threshold) bỏ qua nhiễu nhỏ. Bán kính nhân scale.
/// </summary>
public sealed class SharpenOp : IEditOp
{
    public const string Type = "Sharpen";
    public string OpType => Type;
    public float Amount;        // [0..1] (thường), map ra hệ số mạnh
    public float Radius = 1.0f; // px ở full-res
    public float Threshold;     // [0..1] bỏ qua chi tiết nhỏ hơn ngưỡng

    public bool IsIdentity => MathF.Abs(Amount) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float radius = MathF.Max(0.5f, Radius * scale);
        var blur = GaussianBlur.BlurLuminance(image, radius);
        float[] px = image.Pixels;
        float amt = Math.Clamp(Amount, 0f, 1f) * 3f;
        float thr = Math.Clamp(Threshold, 0f, 1f) * 0.1f;

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float lum = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
                if (lum < 1e-5f) continue;
                float detail = lum - blur[row + x];
                if (MathF.Abs(detail) < thr) continue;
                float newLum = lum + detail * amt;
                if (newLum < 0f) newLum = 0f;
                float gain = newLum / lum;
                px[p] *= gain; px[p + 1] *= gain; px[p + 2] *= gain;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["radius"] = F(Radius), ["threshold"] = F(Threshold),
    };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static SharpenOp FromParams(IReadOnlyDictionary<string, string> p)
        => new()
        {
            Amount = EditOpRegistry.F(p, "amount"),
            Radius = EditOpRegistry.F(p, "radius", 1f),
            Threshold = EditOpRegistry.F(p, "threshold"),
        };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Post-crop Vignette: tối/sáng dần ở rìa theo khoảng cách tới tâm (đơn vị chuẩn hoá nên
/// độc lập với kích thước/scale). amount [-1..1] âm = tối rìa, dương = sáng rìa.
/// midpoint điều chỉnh bán kính bắt đầu, feather độ mượt.
/// </summary>
public sealed class VignetteOp : IEditOp
{
    public const string Type = "Vignette";
    public string OpType => Type;
    public float Amount;          // [-1..1]
    public float Midpoint = 0.5f; // [0..1]
    public float Feather = 0.5f;  // [0..1]

    public bool IsIdentity => MathF.Abs(Amount) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        float cx = w * 0.5f, cy = h * 0.5f;
        float maxDist = MathF.Sqrt(cx * cx + cy * cy);
        float amt = Math.Clamp(Amount, -1f, 1f);
        float mid = Math.Clamp(Midpoint, 0f, 1f);
        float feather = Math.Clamp(Feather, 0.01f, 1f);
        float start = mid;             // bắt đầu mờ ở bán kính chuẩn hoá này
        float end = mid + feather;     // tối đa hiệu ứng

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            float dy = (y - cy);
            for (int x = 0; x < w; x++)
            {
                float dx = (x - cx);
                float d = MathF.Sqrt(dx * dx + dy * dy) / maxDist; // [0..1]
                float t = (d - start) / (end - start);
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                float s = t * t * (3f - 2f * t); // smoothstep
                float factor = 1f + amt * s;     // amt<0: tối rìa
                if (factor < 0f) factor = 0f;
                int p = (row + x) * 4;
                px[p] *= factor; px[p + 1] *= factor; px[p + 2] *= factor;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["midpoint"] = F(Midpoint), ["feather"] = F(Feather),
    };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static VignetteOp FromParams(IReadOnlyDictionary<string, string> p)
        => new()
        {
            Amount = EditOpRegistry.F(p, "amount"),
            Midpoint = EditOpRegistry.F(p, "midpoint", 0.5f),
            Feather = EditOpRegistry.F(p, "feather", 0.5f),
        };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
