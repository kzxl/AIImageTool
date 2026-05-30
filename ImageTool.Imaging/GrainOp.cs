using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Film grain: cộng nhiễu giả ngẫu nhiên (deterministic theo seed + toạ độ) vào luminance.
/// Amount = cường độ, Size = kích thước hạt (px ở full-res, nhân scale), Roughness = độ tương phản hạt.
/// Hạt mạnh hơn ở midtone (giống film thật), yếu ở vùng quá sáng/tối.
///
/// Color [0..1]: pha thêm nhiễu MÀU (chromatic grain) — 0 = grain xám đơn sắc (cũ), &gt;0 = mỗi kênh R/G/B
/// có thành phần nhiễu riêng (giống hạt phim màu). Backward-compatible: mặc định 0.
/// </summary>
public sealed class GrainOp : IEditOp
{
    public const string Type = "Grain";
    public string OpType => Type;
    public float Amount;      // [0..1]
    public float Size = 1f;   // [0.5..5] px
    public float Roughness = 0.5f;
    public float Color;       // [0..1] mức nhiễu màu (0 = đơn sắc)
    public int Seed = 1234;

    public bool IsIdentity => Amount < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        float amt = Math.Clamp(Amount, 0f, 1f) * 0.25f;
        float cellF = MathF.Max(1f, Size * scale); // pixel mỗi hạt
        int cell = (int)MathF.Round(cellF);
        if (cell < 1) cell = 1;
        float rough = 0.5f + Math.Clamp(Roughness, 0f, 1f);
        float colorAmt = Math.Clamp(Color, 0f, 1f);
        int seed = Seed;

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            int gy = y / cell;
            for (int x = 0; x < w; x++)
            {
                int gx = x / cell;
                float n = Hash(gx, gy, seed); // [-1..1]
                n = MathF.Sign(n) * MathF.Pow(MathF.Abs(n), 1f / rough);

                int p = (row + x) * 4;
                float lum = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
                // mạnh nhất ở midtone (0.5), yếu ở 0 và 1.
                float weight = 1f - MathF.Abs(2f * Math.Clamp(lum, 0f, 1f) - 1f);
                float delta = n * amt * weight;
                if (colorAmt > 1e-4f)
                {
                    // Nhiễu riêng từng kênh (dùng seed lệch) trộn với nhiễu chung theo colorAmt.
                    float nr = Hash(gx, gy, seed + 17);
                    float ng = Hash(gx, gy, seed + 101);
                    float nb = Hash(gx, gy, seed + 251);
                    float dr = (n * (1f - colorAmt) + nr * colorAmt) * amt * weight;
                    float dg = (n * (1f - colorAmt) + ng * colorAmt) * amt * weight;
                    float db = (n * (1f - colorAmt) + nb * colorAmt) * amt * weight;
                    px[p] += dr; px[p + 1] += dg; px[p + 2] += db;
                }
                else
                {
                    px[p] += delta; px[p + 1] += delta; px[p + 2] += delta;
                }
                if (px[p] < 0f) px[p] = 0f; if (px[p + 1] < 0f) px[p + 1] = 0f; if (px[p + 2] < 0f) px[p + 2] = 0f;
            }
        });
    }

    // Hash deterministic -> [-1..1].
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint hsh = (uint)(x * 374761393 + y * 668265263 + seed * 1013904223);
            hsh = (hsh ^ (hsh >> 13)) * 1274126177u;
            hsh ^= hsh >> 16;
            return (hsh / (float)uint.MaxValue) * 2f - 1f;
        }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["size"] = F(Size), ["roughness"] = F(Roughness), ["color"] = F(Color),
    };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    public static GrainOp FromParams(IReadOnlyDictionary<string, string> p)
        => new()
        {
            Amount = EditOpRegistry.F(p, "amount"),
            Size = EditOpRegistry.F(p, "size", 1f),
            Roughness = EditOpRegistry.F(p, "roughness", 0.5f),
            Color = EditOpRegistry.F(p, "color"),
        };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
