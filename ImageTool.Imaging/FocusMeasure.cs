using System;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Đo độ NÉT (focus measure) — phát hiện vùng lấy nét / ảnh out nét. KHÔNG sửa được ảnh đã mờ
/// (thông tin đã mất khi chụp); dùng để: (a) chấm điểm nét toàn ảnh để lọc/cull ảnh out nét,
/// (b) sinh "focus map" cho focus peaking, (c) làm trọng số cho Focus Stacking.
///
/// 2 chỉ số kinh điển:
///  - Variance of Laplacian: phương sai của Laplacian (nhạy, dùng rộng rãi để dò blur).
///  - Tenengrad: trung bình bình phương biên độ gradient Sobel (năng lượng cạnh).
/// Cả hai chạy trên luminance (sRGB perceptual). Thuần toán -> test được.
/// </summary>
public static class FocusMeasure
{
    /// <summary>Luminance (sRGB perceptual) của ảnh -> plane float W*H.</summary>
    public static float[] ToGray(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        float[] px = img.Pixels;
        var g = new float[w * h];
        for (int i = 0, p = 0; i < w * h; i++, p += 4)
            g[i] = ColorSpace.LinearToSrgb(ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]));
        return g;
    }

    /// <summary>Laplacian (4-lân cận) của plane gray.</summary>
    public static float[] Laplacian(float[] s, int w, int h)
    {
        var d = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int c = row + x;
                float cc = s[c];
                float up = s[y > 0 ? c - w : c];
                float dn = s[y < h - 1 ? c + w : c];
                float lf = s[x > 0 ? c - 1 : c];
                float rt = s[x < w - 1 ? c + 1 : c];
                d[c] = up + dn + lf + rt - 4f * cc;
            }
        });
        return d;
    }

    /// <summary>Điểm nét toàn ảnh = phương sai của Laplacian. Lớn = nét, nhỏ = mờ/out nét.</summary>
    public static double VarianceOfLaplacian(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var lap = Laplacian(ToGray(img), w, h);
        double mean = 0; for (int i = 0; i < lap.Length; i++) mean += lap[i];
        mean /= lap.Length;
        double var = 0; for (int i = 0; i < lap.Length; i++) { double d = lap[i] - mean; var += d * d; }
        return var / lap.Length;
    }

    /// <summary>Điểm nét Tenengrad = trung bình (Gx^2 + Gy^2) qua Sobel.</summary>
    public static double Tenengrad(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var g = ToGray(img);
        double sum = 0;
        // bỏ viền 1px.
        object lockObj = new();
        Parallel.For(1, h - 1, () => 0.0, (y, _, local) =>
        {
            int row = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                int c = row + x;
                float gx = (g[c - w + 1] + 2f * g[c + 1] + g[c + w + 1])
                         - (g[c - w - 1] + 2f * g[c - 1] + g[c + w - 1]);
                float gy = (g[c + w - 1] + 2f * g[c + w] + g[c + w + 1])
                         - (g[c - w - 1] + 2f * g[c - w] + g[c - w + 1]);
                local += gx * gx + gy * gy;
            }
            return local;
        }, local => { lock (lockObj) sum += local; });
        long count = (long)Math.Max(1, (w - 2)) * Math.Max(1, (h - 2));
        return sum / count;
    }

    /// <summary>
    /// "Focus map" cho peaking: |Laplacian| làm mịn cục bộ, chuẩn hoá [0..1]. Pixel cao = vùng nét.
    /// </summary>
    public static float[] FocusMap(LinearImage img, float blurRadius = 2f)
    {
        int w = img.Width, h = img.Height;
        var lap = Laplacian(ToGray(img), w, h);
        var mag = new float[w * h];
        for (int i = 0; i < mag.Length; i++) mag[i] = MathF.Abs(lap[i]);
        var sm = GaussianBlur.BlurPlane(mag, w, h, MathF.Max(0.5f, blurRadius));
        float max = 1e-6f;
        for (int i = 0; i < sm.Length; i++) if (sm[i] > max) max = sm[i];
        float inv = 1f / max;
        for (int i = 0; i < sm.Length; i++) sm[i] = Math.Clamp(sm[i] * inv, 0f, 1f);
        return sm;
    }

    /// <summary>
    /// Phân loại nhanh ảnh có bị out nét không, theo ngưỡng variance-of-Laplacian.
    /// Ngưỡng mặc định ~ kinh nghiệm cho ảnh [0..1] sRGB; tuỳ nguồn ảnh có thể chỉnh.
    /// </summary>
    public static bool IsBlurry(LinearImage img, double threshold = 1e-4)
        => VarianceOfLaplacian(img) < threshold;
}
