using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class HistogramDataTests
{
    private static LinearImage SolidSrgb(float srgb, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        float lin = ColorSpace.SrgbToLinear(srgb);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = lin; img.Pixels[i + 1] = lin; img.Pixels[i + 2] = lin; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Compute_CountsAllPixels()
    {
        var h = HistogramData.Compute(SolidSrgb(0.5f, 8, 8));
        long sum = 0;
        for (int i = 0; i < 256; i++) sum += h.R[i];
        Assert.Equal(64, sum);
        Assert.Equal(64, h.PixelCount);
    }

    [Fact]
    public void Compute_MidGray_NoClip()
    {
        var h = HistogramData.Compute(SolidSrgb(0.5f));
        Assert.False(h.HighlightClipWarning);
        Assert.False(h.ShadowClipWarning);
    }

    [Fact]
    public void Compute_White_HighlightClip()
    {
        var h = HistogramData.Compute(SolidSrgb(1.0f));
        Assert.True(h.HighlightClipWarning);
        Assert.True(h.HighlightClipPercent > 99.0);
    }

    [Fact]
    public void Compute_Black_ShadowClip()
    {
        var h = HistogramData.Compute(SolidSrgb(0.0f));
        Assert.True(h.ShadowClipWarning);
        Assert.True(h.ShadowClipPercent > 99.0);
    }

    [Fact]
    public void Compute_WhiteBinIs255()
    {
        var h = HistogramData.Compute(SolidSrgb(1.0f, 4, 4));
        Assert.Equal(16, h.R[255]);
        Assert.Equal(0, h.R[0]);
    }

    [Fact]
    public void ComputeBgra_MatchesPixelCount()
    {
        int w = 4, h = 4, stride = w * 4;
        var buf = new byte[stride * h];
        for (int i = 0; i < buf.Length; i += 4) { buf[i] = 128; buf[i + 1] = 128; buf[i + 2] = 128; buf[i + 3] = 255; }
        var hist = HistogramData.ComputeBgra(buf, w, h, stride);
        Assert.Equal(16, hist.PixelCount);
        Assert.Equal(16, hist.R[128]);
    }

    [Fact]
    public void MaxBin_ReturnsPeak()
    {
        var h = HistogramData.Compute(SolidSrgb(0.5f, 10, 10));
        // 100 pixel cùng 1 mức -> max bin = 100.
        Assert.Equal(100, h.MaxBin());
    }
}
