using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Diffuse or Sharpen (D3.1, kiểu Darktable "diffuse or sharpen"): bộ lọc khuếch tán dẫn hướng
/// (anisotropic diffusion, Perona–Malik) trên độ sáng, dùng cho cả SHARPEN bám cạnh (không khuếch
/// đại nhiễu) lẫn DENOISE/làm mịn giữ cạnh, tuỳ dấu Amount.
///
/// Cơ chế: chạy N vòng khuếch tán P–M trên luminance -> bản "Yd" (mượt bám cạnh). Sau đó:
///   Amount &gt; 0  (sharpen): newY = Y + Amount * (Y - Yd)   -> high-pass bám cạnh.
///   Amount &lt; 0  (denoise): newY = lerp(Y, Yd, |Amount|)    -> trộn về bản đã khuếch tán.
/// Rồi nhân gain newY/Y cho cả 3 kênh RGB -> giữ màu (hue), chỉ đổi độ sáng/độ nét.
///
/// Tham số: Amount [-1..1]; Iterations (số vòng PDE, mặc định 6); EdgeSensitivity [0..1]
/// (cao = bám cạnh chặt, ít lan qua biên). Iterations nhân theo scale để preview/full-res nhất quán.
/// </summary>
public sealed class DiffuseOp : IEditOp
{
    public const string Type = "Diffuse";
    public string OpType => Type;

    public float Amount;                 // [-1..1] dương=sharpen, âm=denoise
    public int Iterations = 6;           // số vòng khuếch tán (ở full-res)
    public float EdgeSensitivity = 0.5f; // [0..1]

    public bool IsIdentity => MathF.Abs(Amount) < 1e-4f || Iterations <= 0;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        if (w < 3 || h < 3) return;
        float[] px = image.Pixels;
        float amt = Math.Clamp(Amount, -1f, 1f);

        // số vòng theo scale (proxy nhỏ -> ít vòng hơn để khớp bán kính hiệu dụng).
        int iters = Math.Max(1, (int)MathF.Round(Iterations * MathF.Max(scale, 0.25f)));

        // K (ngưỡng cạnh) theo EdgeSensitivity: cao -> K nhỏ -> bám cạnh chặt.
        float K = 0.25f - Math.Clamp(EdgeSensitivity, 0f, 1f) * 0.22f; // [0.03..0.25]
        float invK2 = 1f / MathF.Max(1e-4f, K * K);
        const float dt = 0.18f; // bước thời gian ổn định (<0.25 cho 4 lân cận)

        // luminance gốc + bản khuếch tán.
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

        var u = (float[])Y.Clone();
        var next = new float[w * h];
        for (int it = 0; it < iters; it++)
        {
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int c = row + x;
                    float uc = u[c];
                    int yn = y > 0 ? c - w : c;
                    int ys = y < h - 1 ? c + w : c;
                    int xe = x < w - 1 ? c + 1 : c;
                    int xw = x > 0 ? c - 1 : c;
                    float dN = u[yn] - uc, dS = u[ys] - uc, dE = u[xe] - uc, dW = u[xw] - uc;
                    // hệ số dẫn (Perona–Malik): gần cạnh (|d| lớn) -> g nhỏ -> ít khuếch tán.
                    float gN = 1f / (1f + dN * dN * invK2);
                    float gS = 1f / (1f + dS * dS * invK2);
                    float gE = 1f / (1f + dE * dE * invK2);
                    float gW = 1f / (1f + dW * dW * invK2);
                    next[c] = uc + dt * (gN * dN + gS * dS + gE * dE + gW * dW);
                }
            });
            (u, next) = (next, u);
        }
        // u = luminance đã khuếch tán (Yd).

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int c = row + x;
                int p = c * 4;
                float yc = Y[c];
                if (yc < 1e-5f) continue;
                float yd = u[c];
                float newY;
                if (amt >= 0f) newY = yc + amt * (yc - yd);     // sharpen bám cạnh
                else newY = yc + (-amt) * (yd - yc);            // denoise (trộn về Yd)
                if (newY < 0f) newY = 0f;
                float gain = newY / yc;
                px[p] *= gain; px[p + 1] *= gain; px[p + 2] *= gain;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["iters"] = Iterations.ToString(CultureInfo.InvariantCulture), ["edge"] = F(EdgeSensitivity),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static DiffuseOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Amount = EditOpRegistry.F(p, "amount"),
        Iterations = EditOpRegistry.I(p, "iters", 6),
        EdgeSensitivity = EditOpRegistry.F(p, "edge", 0.5f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
