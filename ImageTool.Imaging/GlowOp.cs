using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Glow / Soften (kiểu Darktable "soften" / hiệu ứng Orton): tạo lớp mờ (Gaussian) rồi SCREEN-blend
/// trở lại ảnh gốc để cho ánh sáng "loang" mềm mại, mơ màng — thường dùng cho chân dung/phong cảnh.
///
/// Cơ chế (trên linear): blur RGB bán kính lớn -> lớp glow; kết quả = screen(orig, glow) nội suy theo
/// Amount. Screen trong linear: out = 1 - (1-a)*(1-b). Bán kính theo scale để preview/full-res nhất quán.
///
/// Amount [0..1]: cường độ trộn. Radius (px ở full-res, mặc định 12). Threshold [0..1]: chỉ cho vùng
/// sáng hơn ngưỡng tham gia glow (0 = cả ảnh).
/// </summary>
public sealed class GlowOp : IEditOp
{
    public const string Type = "Glow";
    public string OpType => Type;

    public float Amount;            // [0..1]
    public float BaseRadius = 12f;  // px ở full-res
    public float Threshold = 0f;    // [0..1] ngưỡng sáng tối thiểu để glow

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        float radius = MathF.Max(1f, BaseRadius * scale);
        float amt = Math.Clamp(Amount, 0f, 1f);
        float thr = Math.Clamp(Threshold, 0f, 1f);

        // tách 3 kênh (chỉ phần vượt ngưỡng sáng) để blur.
        var rA = new float[w * h];
        var gA = new float[w * h];
        var bA = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float r = px[p], g = px[p + 1], b = px[p + 2];
                if (thr > 0f)
                {
                    // soft bright-pass theo luminance.
                    float lum = ColorSpace.LinearToSrgb(ColorSpace.Luminance(r, g, b));
                    float k = Smoothstep(thr, MathF.Min(1f, thr + 0.2f), lum);
                    r *= k; g *= k; b *= k;
                }
                rA[row + x] = r; gA[row + x] = g; bA[row + x] = b;
            }
        });

        var rB = GaussianBlur.BlurPlane(rA, w, h, radius);
        var gB = GaussianBlur.BlurPlane(gA, w, h, radius);
        var bB = GaussianBlur.BlurPlane(bA, w, h, radius);

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                int p = idx * 4;
                px[p] = ScreenBlend(px[p], rB[idx], amt);
                px[p + 1] = ScreenBlend(px[p + 1], gB[idx], amt);
                px[p + 2] = ScreenBlend(px[p + 2], bB[idx], amt);
            }
        });
    }

    // screen(a,b) trong linear, nội suy về gốc theo amount.
    private static float ScreenBlend(float a, float glow, float amt)
    {
        float ca = a < 0f ? 0f : a;
        float cg = glow < 0f ? 0f : glow;
        float screen = 1f - (1f - ca) * (1f - cg);
        float outv = a + (screen - a) * amt;
        return outv < 0f ? 0f : outv;
    }

    private static float Smoothstep(float e0, float e1, float x)
    {
        if (e1 <= e0) return x >= e1 ? 1f : 0f;
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["radius"] = F(BaseRadius), ["threshold"] = F(Threshold),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static GlowOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Amount = EditOpRegistry.F(p, "amount"),
        BaseRadius = EditOpRegistry.F(p, "radius", 12f),
        Threshold = EditOpRegistry.F(p, "threshold"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
