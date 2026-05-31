using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class BlindWatermarkTests
{
    // Tao anh "that" co ket cau (gradient + chess) de DCT co he so tan trung khac 0.
    private static LinearImage MakeTextured(int w, int h)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float baseV = 0.3f + 0.4f * ((x + y) % 64) / 64f;
                float chess = ((x / 8 + y / 8) % 2 == 0) ? 0.05f : -0.05f;
                float v = ColorSpace.SrgbToLinear(System.Math.Clamp(baseV + chess, 0f, 1f));
                p[o] = v; p[o + 1] = v * 0.9f; p[o + 2] = v * 0.8f; p[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void EmbedExtract_RoundTrips()
    {
        var img = MakeTextured(128, 128);
        int blocks = BlindWatermark.Embed(img, "Hello WM");
        Assert.True(blocks > 0);
        var got = BlindWatermark.Extract(img);
        Assert.Equal("Hello WM", got);
    }

    [Fact]
    public void EmbedExtract_Unicode()
    {
        var img = MakeTextured(160, 160);
        BlindWatermark.Embed(img, "© 2026 Phong");
        Assert.Equal("© 2026 Phong", BlindWatermark.Extract(img));
    }

    [Fact]
    public void EmbedExtract_EmptyMessage()
    {
        var img = MakeTextured(64, 64);
        BlindWatermark.Embed(img, "");
        Assert.Equal("", BlindWatermark.Extract(img));
    }

    [Fact]
    public void Extract_NoWatermark_ReturnsNull()
    {
        var img = MakeTextured(96, 96);
        // Khong nhung gi -> khong co magic header -> null.
        Assert.Null(BlindWatermark.Extract(img));
    }

    [Fact]
    public void Embed_IsNearlyInvisible()
    {
        var orig = MakeTextured(128, 128);
        var wm = orig.Clone();
        BlindWatermark.Embed(wm, "secret");
        // Sai khac trung binh tren kenh sRGB phai nho (watermark vo hinh).
        double sum = 0; int n = orig.Width * orig.Height;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            float a = ColorSpace.LinearToSrgb(orig.Pixels[o]);
            float b = ColorSpace.LinearToSrgb(wm.Pixels[o]);
            sum += System.Math.Abs(a - b);
        }
        double meanDiff = sum / n;
        Assert.True(meanDiff < 0.05, $"sai khac trung binh {meanDiff:F4} qua lon, watermark khong con vo hinh");
    }

    [Fact]
    public void Survives_MildNoise()
    {
        var img = MakeTextured(192, 192);
        BlindWatermark.Embed(img, "robust");
        // Them nhieu nhe (gia lap nen JPEG): +/- mot luong nho ngau nhien deterministic.
        var p = img.Pixels;
        for (int i = 0; i < img.Width * img.Height; i++)
        {
            int o = i * 4;
            float jitter = (((i * 1103515245 + 12345) >> 16) & 0xFF) / 255f - 0.5f;
            jitter *= 0.01f; // ~2.5/255 sRGB
            for (int c = 0; c < 3; c++)
            {
                float s = ColorSpace.LinearToSrgb(p[o + c]) + jitter;
                p[o + c] = ColorSpace.SrgbToLinear(System.Math.Clamp(s, 0f, 1f));
            }
        }
        Assert.Equal("robust", BlindWatermark.Extract(img));
    }

    [Fact]
    public void Embed_TooSmallImage_ReturnsZero()
    {
        var img = new LinearImage(4, 4);
        Assert.Equal(0, BlindWatermark.Embed(img, "x"));
    }

    // === Resize-resilient ===

    // Resample song tuyen 1 anh LinearImage (gia lap resize cua trinh xem/upload).
    private static LinearImage Resize(LinearImage src, int nw, int nh)
    {
        var dst = new LinearImage(nw, nh);
        var s = src.Pixels; var d = dst.Pixels;
        int sw = src.Width, sh = src.Height;
        float xr = sw > 1 ? (float)(sw - 1) / System.Math.Max(1, nw - 1) : 0f;
        float yr = sh > 1 ? (float)(sh - 1) / System.Math.Max(1, nh - 1) : 0f;
        for (int y = 0; y < nh; y++)
        {
            float sy = y * yr; int y0 = (int)sy, y1 = System.Math.Min(sh - 1, y0 + 1); float ty = sy - y0;
            for (int x = 0; x < nw; x++)
            {
                float sx = x * xr; int x0 = (int)sx, x1 = System.Math.Min(sw - 1, x0 + 1); float tx = sx - x0;
                for (int c = 0; c < 4; c++)
                {
                    float p00 = s[(y0 * sw + x0) * 4 + c], p10 = s[(y0 * sw + x1) * 4 + c];
                    float p01 = s[(y1 * sw + x0) * 4 + c], p11 = s[(y1 * sw + x1) * 4 + c];
                    float top = p00 + (p10 - p00) * tx, bot = p01 + (p11 - p01) * tx;
                    d[(y * nw + x) * 4 + c] = top + (bot - top) * ty;
                }
            }
        }
        return dst;
    }

    // Anh smooth giong anh that (gradient + sin) - khong co tan so cao 8px gay alias khi downscale.
    private static LinearImage MakeSmooth(int w, int h)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float v = 0.35f + 0.25f * System.MathF.Sin(x * 0.02f) + 0.2f * (float)y / h;
                v = ColorSpace.SrgbToLinear(System.Math.Clamp(v, 0.02f, 0.98f));
                p[o] = v; p[o + 1] = v * 0.92f; p[o + 2] = v * 0.85f; p[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Resilient_RoundTrips()
    {
        var img = MakeTextured(512, 512);
        int blocks = BlindWatermark.EmbedResilient(img, "resilient WM");
        Assert.True(blocks > 0);
        Assert.Equal("resilient WM", BlindWatermark.ExtractResilient(img));
    }

    [Fact]
    public void Resilient_SurvivesDownscaleThenUpscale()
    {
        var img = MakeSmooth(512, 512);
        BlindWatermark.EmbedResilient(img, "©IMG");
        // Gia lap: thu nho xuong 360 roi phong lai 540 (resize deu hai chieu).
        var small = Resize(img, 360, 360);
        var back = Resize(small, 540, 540);
        Assert.Equal("©IMG", BlindWatermark.ExtractResilient(back));
    }

    [Fact]
    public void Resilient_SurvivesModerateDownscale()
    {
        var img = MakeSmooth(640, 480);
        BlindWatermark.EmbedResilient(img, "keep");
        var small = Resize(img, 400, 300); // ~62%
        Assert.Equal("keep", BlindWatermark.ExtractResilient(small));
    }

    [Fact]
    public void Resilient_SurvivesUpscale()
    {
        var img = MakeSmooth(800, 600);
        BlindWatermark.EmbedResilient(img, "up");
        var big = Resize(img, 1000, 750);
        Assert.Equal("up", BlindWatermark.ExtractResilient(big));
    }

    [Fact]
    public void Resilient_NoWatermark_ReturnsNull()
    {
        var img = MakeTextured(256, 256);
        Assert.Null(BlindWatermark.ExtractResilient(img));
    }
}
