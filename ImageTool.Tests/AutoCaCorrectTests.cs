using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class AutoCaCorrectTests
{
    // Ảnh vòng tròn đồng tâm SẮC NÉT (cạnh mạnh mọi hướng). R giãn theo bán kính (CA) so với G.
    private static LinearImage SyntheticCa(float rScaleR, int w = 96, int h = 96)
    {
        var img = new LinearImage(w, h);
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float dx = x - cx, dy = y - cy;
                float rad = MathF.Sqrt(dx * dx + dy * dy);
                float g = Ring(rad);
                float r = Ring(rad / rScaleR); // R giãn -> thấy vòng của bán kính nhỏ hơn
                img.Pixels[o] = r; img.Pixels[o + 1] = g; img.Pixels[o + 2] = g; img.Pixels[o + 3] = 1f;
            }
        return img;

        // vòng sắc nét (step) -> gradient mạnh.
        static float Ring(float rad) => MathF.Sin(rad * 1.2f) > 0 ? 0.8f : 0.2f;
    }

    [Fact]
    public void Estimate_NoCa_NearZero()
    {
        var img = SyntheticCa(1.0f);
        var (red, blue) = AutoCaCorrect.Estimate(img);
        Assert.InRange(red, -0.15f, 0.15f);
        Assert.InRange(blue, -0.15f, 0.15f);
    }

    [Fact]
    public void Estimate_RedStretched_DetectsRedShift()
    {
        var img = SyntheticCa(1.01f);
        var (red, _) = AutoCaCorrect.Estimate(img);
        Assert.True(MathF.Abs(red) > 0.2f, $"phải phát hiện CA kênh đỏ, red={red}");
    }

    [Fact]
    public void Estimate_CorrectionReducesError()
    {
        // Áp op với hệ số ước lượng -> sai khác R-G ở mép giảm so với ảnh gốc.
        var img = SyntheticCa(1.01f);
        var (red, blue) = AutoCaCorrect.Estimate(img);

        float ErrEdge(LinearImage im)
        {
            int w = im.Width, h = im.Height;
            float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
            float norm = 1f / MathF.Sqrt(cx * cx + cy * cy);
            double e = 0; int n = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (MathF.Sqrt(dx * dx + dy * dy) * norm < 0.5f) continue;
                    int o = (y * w + x) * 4;
                    float d = im.Pixels[o] - im.Pixels[o + 1];
                    e += d * d; n++;
                }
            return n > 0 ? (float)(e / n) : 0f;
        }

        float before = ErrEdge(img);
        var corrected = img.Clone();
        new CaCorrectOp { Red = red, Blue = blue }.Apply(corrected, 1f);
        float after = ErrEdge(corrected);
        Assert.True(after < before, $"khử CA phải giảm sai khác R-G: before={before} after={after}");
    }

    [Fact]
    public void Estimate_TinyImage_ReturnsZero()
    {
        var (red, blue) = AutoCaCorrect.Estimate(new LinearImage(8, 8));
        Assert.Equal(0f, red, 3);
        Assert.Equal(0f, blue, 3);
    }
}
