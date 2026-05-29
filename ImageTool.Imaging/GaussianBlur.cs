using System;
using System.Buffers;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Làm mờ Gaussian tách trục (separable) trên kênh độ sáng/RGB của LinearImage.
/// Dùng làm nền cho Clarity / Texture / Sharpening (unsharp mask) — tất cả đều cần 1 bản blur.
/// Chạy trong linear light nên không gây quầng tối/sáng sai như blur trên gamma.
/// Buffer trung gian lấy từ ArrayPool để giảm GC pressure khi kéo slider liên tục.
/// </summary>
public static class GaussianBlur
{
    /// <summary>
    /// Trả về mảng luminance (linear) đã blur, độ dài = W*H. radius tính bằng pixel (đã nhân scale).
    /// </summary>
    public static float[] BlurLuminance(LinearImage img, float radius)
    {
        int w = img.Width, h = img.Height;
        var lum = new float[w * h];
        float[] px = img.Pixels;
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            int o = row * 4;
            for (int x = 0; x < w; x++)
            {
                int p = o + x * 4;
                lum[row + x] = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
            }
        });
        return BlurPlane(lum, w, h, radius);
    }

    /// <summary>Blur 1 mặt phẳng float (W*H) bằng kernel Gaussian tách trục. Trả mảng mới.</summary>
    public static float[] BlurPlane(float[] src, int w, int h, float radius)
    {
        if (radius < 0.5f) return (float[])src.Clone();
        float[] kernel = BuildKernel(radius, out int k);
        // tmp dùng ArrayPool (chỉ là buffer trung gian, không trả ra ngoài).
        float[] tmp = ArrayPool<float>.Shared.Rent(w * h);
        var dst = new float[w * h];
        try
        {
            // Ngang
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float acc = 0f, wsum = 0f;
                    for (int i = -k; i <= k; i++)
                    {
                        int xx = x + i;
                        if (xx < 0) xx = 0; else if (xx >= w) xx = w - 1;
                        float kv = kernel[i + k];
                        acc += src[row + xx] * kv;
                        wsum += kv;
                    }
                    tmp[row + x] = acc / wsum;
                }
            });

            // Dọc
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float acc = 0f, wsum = 0f;
                    for (int i = -k; i <= k; i++)
                    {
                        int yy = y + i;
                        if (yy < 0) yy = 0; else if (yy >= h) yy = h - 1;
                        float kv = kernel[i + k];
                        acc += tmp[yy * w + x] * kv;
                        wsum += kv;
                    }
                    dst[y * w + x] = acc / wsum;
                }
            });
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tmp);
        }
        return dst;
    }

    private static float[] BuildKernel(float radius, out int k)
    {
        float sigma = MathF.Max(0.5f, radius);
        k = Math.Max(1, (int)MathF.Ceiling(sigma * 3f));
        var kernel = new float[2 * k + 1];
        float inv2s2 = 1f / (2f * sigma * sigma);
        for (int i = -k; i <= k; i++)
            kernel[i + k] = MathF.Exp(-(i * i) * inv2s2);
        return kernel;
    }
}
