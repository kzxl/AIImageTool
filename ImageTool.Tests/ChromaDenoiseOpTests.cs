using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ChromaDenoiseOpTests
{
    [Fact]
    public void Identity_WhenZeroAmount()
    {
        Assert.True(new ChromaDenoiseOp { Amount = 0 }.IsIdentity);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(ChromaDenoiseOp.Type));
    }

    [Fact]
    public void RoundTrip()
    {
        var back = ChromaDenoiseOp.FromParams(new ChromaDenoiseOp { Amount = 0.7f, BaseRadius = 6f, EdgeSensitivity = 0.3f }.ToParams());
        Assert.Equal(0.7f, back.Amount, 4);
        Assert.Equal(6f, back.BaseRadius, 4);
        Assert.Equal(0.3f, back.EdgeSensitivity, 4);
    }

    [Fact]
    public void SmoothsChromaNoise_OnFlatLuminance()
    {
        // Luminance đồng đều nhưng chroma nhiễu xen kẽ -> bilateral (luminance guide không cản) làm mượt chroma.
        int w = 8, h = 8;
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                // chroma nhiễu: xen kẽ đỏ/lam quanh xám 0.5; nhưng luminance gần như bằng nhau.
                bool even = ((x + y) & 1) == 0;
                // chọn r,b lệch nhau nhưng giữ luminance ~0.5 bằng cách điều chỉnh g.
                float r = even ? 0.6f : 0.4f;
                float b = even ? 0.4f : 0.6f;
                float g = (0.5f - ColorSpace.LumR * r - ColorSpace.LumB * b) / ColorSpace.LumG;
                img.Pixels[p] = r; img.Pixels[p + 1] = g; img.Pixels[p + 2] = b; img.Pixels[p + 3] = 1f;
            }

        // chroma variance trước.
        float VarCr(LinearImage im)
        {
            double sum = 0, sum2 = 0; int n = 0;
            for (int i = 0; i < im.Pixels.Length; i += 4)
            {
                float yy = ColorSpace.Luminance(im.Pixels[i], im.Pixels[i + 1], im.Pixels[i + 2]);
                float cr = im.Pixels[i] - yy;
                sum += cr; sum2 += cr * cr; n++;
            }
            double mean = sum / n;
            return (float)(sum2 / n - mean * mean);
        }

        float before = VarCr(img);
        new ChromaDenoiseOp { Amount = 1f, BaseRadius = 4f, EdgeSensitivity = 0.2f }.Apply(img, 1f);
        float after = VarCr(img);
        Assert.True(after < before * 0.6f, $"chroma variance should drop: {before} -> {after}");
    }

    [Fact]
    public void PreservesLuminance()
    {
        int w = 6, h = 6;
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                bool even = ((x + y) & 1) == 0;
                img.Pixels[p] = even ? 0.7f : 0.3f;
                img.Pixels[p + 1] = 0.5f;
                img.Pixels[p + 2] = even ? 0.3f : 0.7f;
                img.Pixels[p + 3] = 1f;
            }
        var lumBefore = new float[w * h];
        for (int i = 0, j = 0; i < img.Pixels.Length; i += 4, j++)
            lumBefore[j] = ColorSpace.Luminance(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2]);

        new ChromaDenoiseOp { Amount = 1f, BaseRadius = 3f }.Apply(img, 1f);

        for (int i = 0, j = 0; i < img.Pixels.Length; i += 4, j++)
        {
            float lum = ColorSpace.Luminance(img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2]);
            Assert.Equal(lumBefore[j], lum, 3);
        }
    }
}
