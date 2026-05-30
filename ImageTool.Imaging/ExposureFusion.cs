using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Exposure Fusion (Mertens, Kautz, Van Reeth 2007) — ghép 1 chùm ảnh PHƠI SÁNG KHÁC NHAU (bracket)
/// thành 1 ảnh có dải động cao hơn, KHÔNG cần biết thời gian phơi (khác HDR merge cổ điển). Mỗi pixel
/// được chấm 3 trọng số chất lượng — contrast, saturation, well-exposedness — rồi trộn đa tỉ lệ bằng
/// Laplacian/Gaussian pyramid để không lộ đường ghép.
///
/// Đây là cách "tăng dynamic range THẬT" (lấy chi tiết vùng sáng từ ảnh thiếu sáng + chi tiết vùng
/// tối từ ảnh dư sáng). Đầu vào/ra đều LinearImage cùng kích thước. Thuần thuật toán -> test được.
/// </summary>
public static class ExposureFusion
{
    /// <summary>Trọng số chất lượng (số mũ). 1 = trung tính; tăng để nhấn tiêu chí đó.</summary>
    public sealed class Options
    {
        public float ContrastWeight = 1f;
        public float SaturationWeight = 1f;
        public float ExposednessWeight = 1f;
        public int PyramidLevels = 0; // 0 = tự tính theo kích thước
        public float ExposednessSigma = 0.2f; // độ "rộng" của vùng phơi sáng tốt quanh 0.5
    }

    /// <summary>
    /// Ghép danh sách ảnh (≥2, cùng kích thước) thành 1 LinearImage. Nếu chỉ 1 ảnh -> trả clone.
    /// </summary>
    public static LinearImage Fuse(IReadOnlyList<LinearImage> images, Options? options = null)
    {
        if (images == null || images.Count == 0) throw new ArgumentException("Cần ít nhất 1 ảnh.", nameof(images));
        if (images.Count == 1) return images[0].Clone();

        int w = images[0].Width, h = images[0].Height;
        for (int i = 1; i < images.Count; i++)
            if (images[i].Width != w || images[i].Height != h)
                throw new ArgumentException("Tất cả ảnh phải cùng kích thước.");

        var opt = options ?? new Options();
        int n = images.Count;

        // 1) tính trọng số mỗi ảnh (W*H), chuẩn hoá tổng = 1 theo pixel.
        var weights = new float[n][];
        for (int i = 0; i < n; i++) weights[i] = ComputeWeights(images[i], opt);
        NormalizeWeights(weights, w, h);

        // 2) số tầng pyramid.
        int levels = opt.PyramidLevels > 0 ? opt.PyramidLevels : MaxLevels(w, h);
        levels = Math.Max(1, levels);

        // 3) build: với mỗi kênh, blend Laplacian pyramid theo Gaussian pyramid của weight.
        // result Laplacian pyramid = Σ_i  L_i(image) * G_i(weight)
        var resultLap = new Pyramid[1]; // placeholder
        Pyramid? acc = null;

        // Gaussian pyramid của từng weight + Laplacian pyramid của từng ảnh (theo kênh RGB).
        for (int i = 0; i < n; i++)
        {
            var gw = Pyramid.BuildGaussian(weights[i], w, h, levels);
            var lap = Pyramid.BuildLaplacianRgb(images[i], levels);
            if (acc == null) acc = Pyramid.ZeroLike(lap);
            acc.AddWeighted(lap, gw);
        }

        // 4) collapse -> ảnh kết quả.
        var outImg = acc!.CollapseToImage();
        // clamp âm.
        var px = outImg.Pixels;
        for (int k = 0; k < px.Length; k++) if (px[k] < 0f) px[k] = 0f;
        return outImg;
    }

    // --- trọng số chất lượng ---
    private static float[] ComputeWeights(LinearImage img, Options opt)
    {
        int w = img.Width, h = img.Height;
        float[] px = img.Pixels;

        // grayscale (sRGB perceptual) cho contrast.
        var gray = new float[w * h];
        for (int i = 0, p = 0; i < w * h; i++, p += 4)
            gray[i] = ColorSpace.LinearToSrgb(ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]));

        // contrast = |Laplacian| của gray.
        var contrast = Laplacian(gray, w, h);

        var weight = new float[w * h];
        float cw = opt.ContrastWeight, sw = opt.SaturationWeight, ew = opt.ExposednessWeight;
        float sigma = MathF.Max(1e-3f, opt.ExposednessSigma);
        float inv2s2 = 1f / (2f * sigma * sigma);

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                int p = idx * 4;
                float r = ColorSpace.LinearToSrgb(px[p]);
                float g = ColorSpace.LinearToSrgb(px[p + 1]);
                float b = ColorSpace.LinearToSrgb(px[p + 2]);

                // saturation = std-dev của 3 kênh.
                float mean = (r + g + b) / 3f;
                float sat = MathF.Sqrt(((r - mean) * (r - mean) + (g - mean) * (g - mean) + (b - mean) * (b - mean)) / 3f);

                // well-exposedness = tích Gauss quanh 0.5 cho mỗi kênh.
                float er = MathF.Exp(-(r - 0.5f) * (r - 0.5f) * inv2s2);
                float eg = MathF.Exp(-(g - 0.5f) * (g - 0.5f) * inv2s2);
                float eb = MathF.Exp(-(b - 0.5f) * (b - 0.5f) * inv2s2);
                float exposed = er * eg * eb;

                float c = MathF.Abs(contrast[idx]);
                float wgt = MathF.Pow(MathF.Max(c, 1e-6f), cw)
                          * MathF.Pow(MathF.Max(sat, 1e-6f), sw)
                          * MathF.Pow(MathF.Max(exposed, 1e-6f), ew);
                // epsilon chỉ để tránh 0 tuyệt đối; phải NHỎ HƠN NHIỀU tích trên (vốn ~1e-12 ở vùng
                // phẳng grayscale) để không làm mất khả năng phân biệt theo well-exposedness.
                weight[idx] = wgt + 1e-20f;
            }
        });
        return weight;
    }

    private static void NormalizeWeights(float[][] weights, int w, int h)
    {
        int n = weights.Length;
        int total = w * h;
        Parallel.For(0, total, i =>
        {
            float sum = 0f;
            for (int k = 0; k < n; k++) sum += weights[k][i];
            // ngưỡng fallback PHẢI rất nhỏ: weight ở vùng phẳng grayscale có thể ~1e-12 (do clamp
            // contrast/sat = 1e-6) nhưng vẫn hợp lệ và phân biệt được theo exposedness.
            if (sum < 1e-30f) { for (int k = 0; k < n; k++) weights[k][i] = 1f / n; }
            else { float inv = 1f / sum; for (int k = 0; k < n; k++) weights[k][i] *= inv; }
        });
    }

    private static float[] Laplacian(float[] s, int w, int h)
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

    private static int MaxLevels(int w, int h)
    {
        int m = Math.Min(w, h);
        int levels = 1;
        while (m > 8 && levels < 8) { m /= 2; levels++; }
        return levels;
    }
}
