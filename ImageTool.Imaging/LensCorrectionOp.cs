using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Lens correction thủ công (#4, 5.3 cơ bản) — sửa méo hình (distortion) + tối góc (vignette) bằng
/// tham số, không cần database lensfun. Distortion theo mô hình đa thức bán kính (Brown-Conrady đơn
/// giản): r_src = r_dst * (1 + k1*r² + k2*r⁴), với r chuẩn hoá theo nửa đường chéo. k>0 sửa pincushion,
/// k&lt;0 sửa barrel. VignetteCorrection &gt;0 làm sáng góc (bù tối góc ống kính).
///
/// Là op pixel-remap nhưng GIỮ nguyên W×H (khác Crop) -> dùng Apply (sửa tại chỗ qua buffer phụ).
/// Tham số chuẩn hoá nên khớp proxy/full-res.
/// </summary>
public sealed class LensCorrectionOp : IEditOp
{
    public const string Type = "LensCorrection";
    public string OpType => Type;

    public float K1;                    // [-0.5..0.5] distortion bậc 2
    public float K2;                    // [-0.5..0.5] distortion bậc 4
    public float VignetteCorrection;    // [0..1] bù sáng góc

    public bool IsIdentity =>
        MathF.Abs(K1) < 1e-4f && MathF.Abs(K2) < 1e-4f && MathF.Abs(VignetteCorrection) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;

        bool doDist = MathF.Abs(K1) > 1e-4f || MathF.Abs(K2) > 1e-4f;
        if (doDist)
            RemapDistortion(image, w, h);

        if (MathF.Abs(VignetteCorrection) > 1e-4f)
            ApplyVignetteCorrection(image, w, h);
    }

    private void RemapDistortion(LinearImage image, int w, int h)
    {
        float[] src = (float[])image.Pixels.Clone();
        float[] dst = image.Pixels;
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float halfDiag = MathF.Sqrt(cx * cx + cy * cy);
        if (halfDiag < 1e-3f) return;
        float k1 = K1, k2 = K2;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float ndx = (x - cx) / halfDiag;
                float ndy = (y - cy) / halfDiag;
                float r2 = ndx * ndx + ndy * ndy;
                float factor = 1f + k1 * r2 + k2 * r2 * r2;
                float srcXn = ndx * factor, srcYn = ndy * factor;
                float sx = srcXn * halfDiag + cx;
                float sy = srcYn * halfDiag + cy;
                int o = (y * w + x) * 4;
                SampleBilinear(src, w, h, sx, sy, dst, o);
            }
        });
    }

    private void ApplyVignetteCorrection(LinearImage image, int w, int h)
    {
        float[] px = image.Pixels;
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float maxDist = MathF.Sqrt(cx * cx + cy * cy);
        if (maxDist < 1e-3f) return;
        float amt = Math.Clamp(VignetteCorrection, 0f, 1f);

        Parallel.For(0, h, y =>
        {
            float dy = y - cy;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float dx = x - cx;
                float d = MathF.Sqrt(dx * dx + dy * dy) / maxDist; // 0 tâm .. 1 góc
                // gain tăng theo r² (góc sáng lên nhiều hơn).
                float gain = 1f + amt * d * d;
                int o = (row + x) * 4;
                px[o] *= gain; px[o + 1] *= gain; px[o + 2] *= gain;
            }
        });
    }

    private static void SampleBilinear(float[] s, int sw, int sh, float fx, float fy, float[] d, int o)
    {
        if (fx < 0 || fy < 0 || fx > sw - 1 || fy > sh - 1)
        {
            d[o] = 0; d[o + 1] = 0; d[o + 2] = 0; d[o + 3] = 0; // ngoài biên = trong suốt
            return;
        }
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(sw - 1, x0 + 1), y1 = Math.Min(sh - 1, y0 + 1);
        float tx = fx - x0, ty = fy - y0;
        for (int c = 0; c < 4; c++)
        {
            float p00 = s[(y0 * sw + x0) * 4 + c];
            float p10 = s[(y0 * sw + x1) * 4 + c];
            float p01 = s[(y1 * sw + x0) * 4 + c];
            float p11 = s[(y1 * sw + x1) * 4 + c];
            float top = p00 + (p10 - p00) * tx;
            float bot = p01 + (p11 - p01) * tx;
            d[o + c] = top + (bot - top) * ty;
        }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["k1"] = F(K1), ["k2"] = F(K2), ["vig"] = F(VignetteCorrection),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static LensCorrectionOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        K1 = EditOpRegistry.F(p, "k1"),
        K2 = EditOpRegistry.F(p, "k2"),
        VignetteCorrection = EditOpRegistry.F(p, "vig"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
