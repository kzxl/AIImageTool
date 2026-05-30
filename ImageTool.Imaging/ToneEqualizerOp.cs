using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Tone Equalizer (D1.3, kiểu Darktable "tone equalizer") — chỉnh sáng theo VÙNG độ sáng (zone),
/// dùng luminance đã làm mượt (guided) làm "địa chỉ" để tránh quầng (halo). 5 dải zone:
/// Blacks / Shadows / Midtones / Highlights / Whites, mỗi dải 1 hệ số EV [-1..1] (map ±2 stop).
///
/// Cách làm: tính luminance mượt (Gaussian) -> với mỗi pixel, nội suy gain EV theo trọng số 5 zone
/// tại vị trí tông của nó -> nhân RGB. Vì dùng luma MƯỢT, vùng chuyển mềm, ít halo (giống guided).
/// </summary>
public sealed class ToneEqualizerOp : IEditOp
{
    public const string Type = "ToneEqualizer";
    public string OpType => Type;

    // 5 zone EV [-1..1] (map ra ±2 stop). 0 = không đổi.
    public float Blacks, Shadows, Midtones, Highlights, Whites;
    public float BaseRadius = 20f; // bán kính blur luma (px full-res) -> guided

    public bool IsIdentity =>
        Z(Blacks) && Z(Shadows) && Z(Midtones) && Z(Highlights) && Z(Whites);
    private static bool Z(float v) => MathF.Abs(v) < 1e-4f;

    // tâm tông (sRGB-perceptual) của 5 zone.
    private static readonly float[] Centers = { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f };

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        float radius = MathF.Max(1f, BaseRadius * scale);

        // luma mượt (guidance) để chọn zone — giảm halo so với dùng luma thô.
        var luma = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                luma[row + x] = ColorSpace.LinearToSrgb(ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]));
            }
        });
        var guide = GaussianBlur.BlurPlane(luma, w, h, radius);

        float[] ev = { Blacks, Shadows, Midtones, Highlights, Whites };

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float pos = guide[row + x]; // vị trí tông [0..1] (mượt)
                // trọng số 5 zone (cosine window), chuẩn hoá.
                float wSum = 0f, evMix = 0f;
                for (int z = 0; z < 5; z++)
                {
                    float wt = ZoneWeight(pos, Centers[z]);
                    if (wt <= 0f) continue;
                    wSum += wt;
                    evMix += wt * ev[z];
                }
                if (wSum > 1e-6f) evMix /= wSum;
                if (MathF.Abs(evMix) < 1e-5f) continue;

                float gain = MathF.Pow(2f, evMix * 2f); // ±1 -> ±2 stop
                int p = (row + x) * 4;
                px[p] *= gain; px[p + 1] *= gain; px[p + 2] *= gain;
                if (px[p] < 0f) px[p] = 0f; if (px[p + 1] < 0f) px[p + 1] = 0f; if (px[p + 2] < 0f) px[p + 2] = 0f;
            }
        });
    }

    private static float ZoneWeight(float pos, float center)
    {
        float d = MathF.Abs(pos - center);
        const float window = 0.3f; // dải phủ, có chồng lấn mượt
        if (d >= window) return 0f;
        return 0.5f * (1f + MathF.Cos(MathF.PI * d / window));
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["blacks"] = F(Blacks), ["shadows"] = F(Shadows), ["mid"] = F(Midtones),
        ["highlights"] = F(Highlights), ["whites"] = F(Whites),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static ToneEqualizerOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Blacks = EditOpRegistry.F(p, "blacks"),
        Shadows = EditOpRegistry.F(p, "shadows"),
        Midtones = EditOpRegistry.F(p, "mid"),
        Highlights = EditOpRegistry.F(p, "highlights"),
        Whites = EditOpRegistry.F(p, "whites"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
