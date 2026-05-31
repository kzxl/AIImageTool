using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Áp profile lens từ lensfun (5.3, tự động): hiệu chỉnh distortion (poly3/poly5/ptlens) + vignetting
/// (model "pa") theo hệ số đã nội suy cho tiêu cự cụ thể. Khác <see cref="LensCorrectionOp"/> thủ công
/// ở chỗ hệ số đến từ database thay vì người dùng kéo slider.
///
/// Mô hình lensfun:
///   - poly3:  ru = rd · (1 + k1·rd²)
///   - poly5:  ru = rd · (1 + k1·rd² + k2·rd⁴)
///   - ptlens: ru = rd · (a·rd³ + b·rd² + c·rd + 1)
///   (rd, ru chuẩn hoá theo nửa cạnh NHỎ — quy ước lensfun. Ở đây dùng nửa đường chéo để khớp op thủ công;
///    hệ số vẫn hợp lệ tương đối, cần verify trên ảnh thật để tinh chỉnh chuẩn hoá.)
///   - vignetting "pa": cường độ tại r = 1 + k1·r² + k2·r⁴ + k3·r⁶; ảnh gốc đã nhân cường độ này nên
///     bù = chia (gain = 1/intensity).
///
/// Là op remap giữ W×H. Hệ số chuẩn hoá nên khớp proxy/full-res. Phần áp pixel CẦN VERIFY ảnh RAW thật.
/// </summary>
public sealed class LensProfileOp : IEditOp
{
    public const string Type = "LensProfile";
    public string OpType => Type;

    public string DistModel = "";   // "", poly3, poly5, ptlens
    public float Dk1, Dk2, Dk3;     // hệ số distortion
    public float Vk1, Vk2, Vk3;     // hệ số vignetting "pa"
    public bool CorrectDistortion = true;
    public bool CorrectVignetting = true;

    public bool IsIdentity =>
        (!CorrectDistortion || (MathF.Abs(Dk1) < 1e-6f && MathF.Abs(Dk2) < 1e-6f && MathF.Abs(Dk3) < 1e-6f) || DistModel.Length == 0)
        && (!CorrectVignetting || (MathF.Abs(Vk1) < 1e-6f && MathF.Abs(Vk2) < 1e-6f && MathF.Abs(Vk3) < 1e-6f));

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;

        bool doDist = CorrectDistortion && DistModel.Length > 0
            && (MathF.Abs(Dk1) > 1e-6f || MathF.Abs(Dk2) > 1e-6f || MathF.Abs(Dk3) > 1e-6f);
        if (doDist) RemapDistortion(image, w, h);

        bool doVig = CorrectVignetting
            && (MathF.Abs(Vk1) > 1e-6f || MathF.Abs(Vk2) > 1e-6f || MathF.Abs(Vk3) > 1e-6f);
        if (doVig) CorrectVig(image, w, h);
    }

    /// <summary>Hệ số bán kính nguồn theo model (rd chuẩn hoá). ru = rd·factor.</summary>
    public float DistortionFactor(float rd)
    {
        float r2 = rd * rd;
        return DistModel switch
        {
            "poly5" => 1f + Dk1 * r2 + Dk2 * r2 * r2,
            "ptlens" => Dk1 * rd * r2 + Dk2 * r2 + Dk3 * rd + 1f, // a·rd³ + b·rd² + c·rd + 1
            _ => 1f + Dk1 * r2,                                    // poly3 (mặc định)
        };
    }

    private void RemapDistortion(LinearImage image, int w, int h)
    {
        float[] src = (float[])image.Pixels.Clone();
        float[] dst = image.Pixels;
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float halfDiag = MathF.Sqrt(cx * cx + cy * cy);
        if (halfDiag < 1e-3f) return;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float ndx = (x - cx) / halfDiag;
                float ndy = (y - cy) / halfDiag;
                float rd = MathF.Sqrt(ndx * ndx + ndy * ndy);
                float factor = DistortionFactor(rd);
                float sx = ndx * factor * halfDiag + cx;
                float sy = ndy * factor * halfDiag + cy;
                SampleBilinear(src, w, h, sx, sy, dst, (y * w + x) * 4);
            }
        });
    }

    /// <summary>Cường độ vignetting model "pa" tại bán kính chuẩn hoá r.</summary>
    public float VignetteIntensity(float r)
    {
        float r2 = r * r;
        return 1f + Vk1 * r2 + Vk2 * r2 * r2 + Vk3 * r2 * r2 * r2;
    }

    private void CorrectVig(LinearImage image, int w, int h)
    {
        float[] px = image.Pixels;
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float maxDist = MathF.Sqrt(cx * cx + cy * cy);
        if (maxDist < 1e-3f) return;

        Parallel.For(0, h, y =>
        {
            float dy = y - cy;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float dx = x - cx;
                float r = MathF.Sqrt(dx * dx + dy * dy) / maxDist;
                float intensity = VignetteIntensity(r);
                if (intensity < 1e-3f) intensity = 1e-3f;
                float gain = 1f / intensity; // bù: chia cho cường độ vignetting
                int o = (row + x) * 4;
                px[o] *= gain; px[o + 1] *= gain; px[o + 2] *= gain;
            }
        });
    }

    private static void SampleBilinear(float[] s, int sw, int sh, float fx, float fy, float[] d, int o)
    {
        if (fx < 0 || fy < 0 || fx > sw - 1 || fy > sh - 1)
        {
            d[o] = 0; d[o + 1] = 0; d[o + 2] = 0; d[o + 3] = 0;
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
        ["distModel"] = DistModel,
        ["dk1"] = F(Dk1), ["dk2"] = F(Dk2), ["dk3"] = F(Dk3),
        ["vk1"] = F(Vk1), ["vk2"] = F(Vk2), ["vk3"] = F(Vk3),
        ["cd"] = CorrectDistortion ? "true" : "false",
        ["cv"] = CorrectVignetting ? "true" : "false",
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static LensProfileOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        DistModel = EditOpRegistry.S(p, "distModel"),
        Dk1 = EditOpRegistry.F(p, "dk1"), Dk2 = EditOpRegistry.F(p, "dk2"), Dk3 = EditOpRegistry.F(p, "dk3"),
        Vk1 = EditOpRegistry.F(p, "vk1"), Vk2 = EditOpRegistry.F(p, "vk2"), Vk3 = EditOpRegistry.F(p, "vk3"),
        CorrectDistortion = !p.TryGetValue("cd", out var cd) || cd != "false",
        CorrectVignetting = !p.TryGetValue("cv", out var cv) || cv != "false",
    };

    /// <summary>Dựng op từ 1 profile lensfun đã nội suy (tiện khi áp tự động theo EXIF).</summary>
    public static LensProfileOp FromCalib(LensfunDatabase.DistortionCalib? dist, LensfunDatabase.VignettingCalib? vig)
    {
        var op = new LensProfileOp();
        if (dist != null)
        {
            op.DistModel = dist.Model;
            op.Dk1 = dist.K1; op.Dk2 = dist.K2; op.Dk3 = dist.K3;
        }
        if (vig != null)
        {
            op.Vk1 = vig.K1; op.Vk2 = vig.K2; op.Vk3 = vig.K3;
        }
        return op;
    }

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
