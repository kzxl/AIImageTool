using System;

namespace ImageTool.Imaging;

/// <summary>
/// Phân tích ảnh (linear) và đề xuất chỉnh Basic tự động — tương tự nút "Auto" của Lightroom.
/// Chiến lược đơn giản, ổn định: tính histogram luminance (trên sRGB-perceptual), tìm điểm
/// đen/trắng theo phân vị (percentile) để kéo dải động, ước lượng exposure để đưa trung vị về
/// midtone mục tiêu, và đặt contrast nhẹ nếu ảnh phẳng.
/// </summary>
public static class AutoTone
{
    /// <summary>Kết quả gợi ý: các trường khớp DevelopBasicOp.</summary>
    public struct Suggestion
    {
        public float Exposure;
        public float Contrast;
        public float Whites;
        public float Blacks;
        public float Shadows;
        public float Highlights;
    }

    public static Suggestion Analyze(LinearImage img)
    {
        const int N = 256;
        var hist = new int[N];
        float[] px = img.Pixels;
        int count = 0;

        // Histogram luminance perceptual.
        for (int o = 0; o < px.Length; o += 4)
        {
            float lum = ColorSpace.Luminance(px[o], px[o + 1], px[o + 2]);
            float p = ColorSpace.LinearToSrgb(lum);
            int bin = (int)(p * (N - 1) + 0.5f);
            if (bin < 0) bin = 0; else if (bin >= N) bin = N - 1;
            hist[bin]++;
            count++;
        }
        if (count == 0) return default;

        // Percentile helpers.
        float lowP = Percentile(hist, count, 0.005f);   // 0.5%
        float highP = Percentile(hist, count, 0.995f);  // 99.5%
        float median = Percentile(hist, count, 0.5f);

        var s = new Suggestion();

        // Exposure: đưa median về ~0.45 (midtone) — tính theo stops trong linear.
        float medLin = MathF.Max(1e-4f, ColorSpace.SrgbToLinear(median));
        float targetLin = ColorSpace.SrgbToLinear(0.45f);
        float ev = MathF.Log2(targetLin / medLin);
        s.Exposure = Math.Clamp(ev, -2f, 2f);

        // Blacks/Whites: kéo điểm đen/trắng dựa trên đuôi histogram.
        // Nếu điểm đen cao (ảnh thiếu vùng tối) -> hạ blacks; điểm trắng thấp -> nâng whites.
        s.Blacks = Math.Clamp(-lowP * 2f, -1f, 0f);          // lowP>0 -> kéo đen xuống
        s.Whites = Math.Clamp((1f - highP) * 2f, 0f, 1f);    // highP<1 -> kéo trắng lên

        // Contrast: nếu dải động hẹp (ảnh phẳng) thì tăng nhẹ.
        float spread = highP - lowP;
        s.Contrast = spread < 0.6f ? Math.Clamp((0.6f - spread) * 1.5f, 0f, 0.4f) : 0f;

        // Shadows/Highlights: phục hồi nhẹ nếu lệch.
        s.Shadows = median < 0.35f ? Math.Clamp((0.35f - median) * 1.2f, 0f, 0.4f) : 0f;
        s.Highlights = median > 0.65f ? -Math.Clamp((median - 0.65f) * 1.2f, 0f, 0.4f) : 0f;

        return s;
    }

    private static float Percentile(int[] hist, int total, float pct)
    {
        int target = (int)(total * pct);
        int acc = 0;
        for (int i = 0; i < hist.Length; i++)
        {
            acc += hist[i];
            if (acc >= target) return i / (float)(hist.Length - 1);
        }
        return 1f;
    }

    /// <summary>Gợi ý cho Levels: black/white point (sRGB) từ đuôi histogram.</summary>
    public struct LevelsSuggestion
    {
        public float Black;
        public float White;
        public float Gamma;
    }

    /// <summary>
    /// Auto Levels (D2.5): chọn điểm đen/trắng input theo phân vị (mặc định 0.5% / 99.5%) để kéo
    /// căng dải động; gamma giữ 1 (không dịch midtone). Trả về điểm trên thang sRGB [0..1].
    /// </summary>
    public static LevelsSuggestion AnalyzeLevels(LinearImage img, float lowPct = 0.005f, float highPct = 0.995f)
    {
        const int N = 256;
        var hist = new int[N];
        float[] px = img.Pixels;
        int count = 0;
        for (int o = 0; o < px.Length; o += 4)
        {
            float lum = ColorSpace.Luminance(px[o], px[o + 1], px[o + 2]);
            float p = ColorSpace.LinearToSrgb(lum);
            int bin = (int)(p * (N - 1) + 0.5f);
            if (bin < 0) bin = 0; else if (bin >= N) bin = N - 1;
            hist[bin]++;
            count++;
        }
        if (count == 0) return new LevelsSuggestion { Black = 0f, White = 1f, Gamma = 1f };

        float black = Percentile(hist, count, lowPct);
        float white = Percentile(hist, count, highPct);
        // Đảm bảo hợp lệ + không thu hẹp quá mức.
        black = Math.Clamp(black, 0f, 0.45f);
        white = Math.Clamp(white, Math.Max(black + 0.05f, 0.55f), 1f);
        return new LevelsSuggestion { Black = black, White = white, Gamma = 1f };
    }
}
