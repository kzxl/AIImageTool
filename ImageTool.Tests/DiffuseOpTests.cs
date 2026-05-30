using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class DiffuseOpTests
{
    private static LinearImage Checker(int w = 8, int h = 8, float lo = 0.3f, float hi = 0.7f)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                float v = ((x + y) & 1) == 0 ? hi : lo;
                img.Pixels[p] = img.Pixels[p + 1] = img.Pixels[p + 2] = v;
                img.Pixels[p + 3] = 1f;
            }
        return img;
    }

    private static LinearImage Edge(int w = 8, int h = 8)
    {
        // nửa trái tối, nửa phải sáng (1 cạnh dọc).
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = (y * w + x) * 4;
                float v = x < w / 2 ? 0.3f : 0.7f;
                img.Pixels[p] = img.Pixels[p + 1] = img.Pixels[p + 2] = v;
                img.Pixels[p + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Identity_WhenZeroAmount()
    {
        Assert.True(new DiffuseOp { Amount = 0 }.IsIdentity);
    }

    [Fact]
    public void Identity_WhenZeroIterations()
    {
        Assert.True(new DiffuseOp { Amount = 0.5f, Iterations = 0 }.IsIdentity);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(DiffuseOp.Type));
    }

    [Fact]
    public void RoundTrip()
    {
        var back = DiffuseOp.FromParams(new DiffuseOp { Amount = -0.4f, Iterations = 8, EdgeSensitivity = 0.7f }.ToParams());
        Assert.Equal(-0.4f, back.Amount, 4);
        Assert.Equal(8, back.Iterations);
        Assert.Equal(0.7f, back.EdgeSensitivity, 4);
    }

    [Fact]
    public void Denoise_ReducesLocalVariance()
    {
        var img = Checker();
        float VarLum(LinearImage im)
        {
            double s = 0, s2 = 0; int n = 0;
            for (int i = 0; i < im.Pixels.Length; i += 4)
            { float v = ColorSpace.Luminance(im.Pixels[i], im.Pixels[i + 1], im.Pixels[i + 2]); s += v; s2 += v * v; n++; }
            double m = s / n; return (float)(s2 / n - m * m);
        }
        float before = VarLum(img);
        new DiffuseOp { Amount = -1f, Iterations = 8, EdgeSensitivity = 0.1f }.Apply(img, 1f);
        float after = VarLum(img);
        Assert.True(after < before, $"denoise should reduce variance: {before} -> {after}");
    }

    [Fact]
    public void Sharpen_IncreasesEdgeContrast()
    {
        var img = Edge();
        // contrast quanh biên: chênh giữa 2 cột giữa.
        int w = 8;
        int leftCol = w / 2 - 1, rightCol = w / 2;
        float Mid(LinearImage im, int col)
        {
            int p = (3 * w + col) * 4;
            return ColorSpace.Luminance(im.Pixels[p], im.Pixels[p + 1], im.Pixels[p + 2]);
        }
        float before = Mid(img, rightCol) - Mid(img, leftCol);
        new DiffuseOp { Amount = 1f, Iterations = 6, EdgeSensitivity = 0.5f }.Apply(img, 1f);
        float after = Mid(img, rightCol) - Mid(img, leftCol);
        Assert.True(after >= before, $"sharpen should not reduce edge contrast: {before} -> {after}");
    }

    [Fact]
    public void PreservesGrayHue()
    {
        // ảnh xám: sau xử lý vẫn xám (gain đều 3 kênh).
        var img = Checker();
        new DiffuseOp { Amount = 0.5f, Iterations = 5 }.Apply(img, 1f);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            Assert.Equal(img.Pixels[i], img.Pixels[i + 1], 4);
            Assert.Equal(img.Pixels[i + 1], img.Pixels[i + 2], 4);
        }
    }
}
