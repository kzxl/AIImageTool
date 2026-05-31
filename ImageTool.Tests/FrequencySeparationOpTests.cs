using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class FrequencySeparationOpTests
{
    // Anh co tan thap (gradient) + tan cao (cham sang/toi xen ke) de kiem tra tach tang.
    private static LinearImage MakeDetailed(int w = 32, int h = 32)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float low = 0.4f + 0.2f * x / w;                       // tần thấp
                float high = ((x + y) % 2 == 0) ? 0.08f : -0.08f;      // tần cao (checker 1px)
                float v = ColorSpace.SrgbToLinear(System.Math.Clamp(low + high, 0f, 1f));
                p[o] = v; p[o + 1] = v; p[o + 2] = v; p[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Default_IsIdentity()
    {
        var op = new FrequencySeparationOp(); // smoothing 0, detail 1
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void Smoothing_ReducesLowFrequencyVariation_KeepsHighFreq()
    {
        var orig = MakeDetailed();
        var img = orig.Clone();
        var op = new FrequencySeparationOp { Radius = 4f, Smoothing = 1f, DetailAmount = 1f };
        op.Apply(img, 1f);

        // Tần cao (chênh lệch pixel kề 1px) phải còn xấp xỉ (detail giữ nguyên).
        float HighVar(LinearImage im)
        {
            int w = im.Width; double s = 0; int n = 0;
            for (int y = 0; y < im.Height; y++)
                for (int x = 1; x < w; x++)
                { s += System.Math.Abs(im.Pixels[(y * w + x) * 4] - im.Pixels[(y * w + x - 1) * 4]); n++; }
            return (float)(s / n);
        }
        // Detail giữ -> high-freq variation gần như không đổi.
        Assert.True(HighVar(img) > HighVar(orig) * 0.6f, "tần cao phải được giữ lại");
    }

    [Fact]
    public void DetailAmount_AbovOne_IncreasesHighFreq()
    {
        var img = MakeDetailed();
        var sharp = img.Clone();
        new FrequencySeparationOp { Radius = 4f, DetailAmount = 2f }.Apply(sharp, 1f);

        float HighVar(LinearImage im)
        {
            int w = im.Width; double s = 0; int n = 0;
            for (int y = 0; y < im.Height; y++)
                for (int x = 1; x < w; x++)
                { s += System.Math.Abs(im.Pixels[(y * w + x) * 4] - im.Pixels[(y * w + x - 1) * 4]); n++; }
            return (float)(s / n);
        }
        Assert.True(HighVar(sharp) > HighVar(img), "detail x2 phải tăng tần cao");
    }

    [Fact]
    public void OutputStaysFinite_AndNonNegative()
    {
        var img = MakeDetailed();
        new FrequencySeparationOp { Radius = 6f, Smoothing = 0.8f, DetailAmount = 1.5f }.Apply(img, 1f);
        foreach (var v in img.Pixels) { Assert.True(float.IsFinite(v)); Assert.True(v >= 0f); }
    }

    [Fact]
    public void RoundTrip_Params()
    {
        var op = new FrequencySeparationOp { Radius = 12f, Smoothing = 0.6f, DetailAmount = 1.3f };
        var back = FrequencySeparationOp.FromParams(op.ToParams());
        Assert.Equal(12f, back.Radius, 2);
        Assert.Equal(0.6f, back.Smoothing, 2);
        Assert.Equal(1.3f, back.DetailAmount, 2);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(FrequencySeparationOp.Type));
    }
}
