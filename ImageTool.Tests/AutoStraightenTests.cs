using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class AutoStraightenTests
{
    // Ảnh biên bậc nghiêng (kiểu đường chân trời): nửa trên sáng, nửa dưới tối, biên nghiêng angleDeg.
    private static LinearImage TiltedHorizon(float angleDeg, int w = 120, int h = 120)
    {
        var img = new LinearImage(w, h);
        float ang = angleDeg * MathF.PI / 180f;
        // pháp tuyến của biên (vuông góc đường nghiêng).
        float nx = -MathF.Sin(ang), ny = MathF.Cos(ang);
        float cx = w / 2f, cy = h / 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = (x - cx) * nx + (y - cy) * ny;
                float v = s > 0 ? 0.85f : 0.15f;
                int o = (y * w + x) * 4;
                img.Pixels[o] = v; img.Pixels[o + 1] = v; img.Pixels[o + 2] = v; img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    [Theory]
    [InlineData(5f)]
    [InlineData(-7f)]
    [InlineData(10f)]
    public void Estimate_DetectsTiltedLine(float tilt)
    {
        var img = TiltedHorizon(tilt);
        float est = AutoStraighten.EstimateAngle(img);
        // Ước lượng phải gần góc nghiêng thật (sai số ≤ 3° do rời rạc hoá + làm mờ).
        Assert.InRange(est, tilt - 3f, tilt + 3f);
    }

    [Fact]
    public void Estimate_HorizontalLine_NearZero()
    {
        var img = TiltedHorizon(0f);
        float est = AutoStraighten.EstimateAngle(img);
        Assert.InRange(est, -1.5f, 1.5f);
    }

    [Fact]
    public void Estimate_FlatImage_ReturnsZero()
    {
        // ảnh phẳng không cạnh -> không có góc dominant -> 0.
        var img = new LinearImage(40, 40);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.5f; img.Pixels[i + 1] = 0.5f; img.Pixels[i + 2] = 0.5f; img.Pixels[i + 3] = 1f; }
        Assert.Equal(0f, AutoStraighten.EstimateAngle(img), 1);
    }

    [Fact]
    public void Estimate_TinyImage_ReturnsZero()
    {
        Assert.Equal(0f, AutoStraighten.EstimateAngle(new LinearImage(4, 4)), 3);
    }

    [Fact]
    public void Estimate_VerticalLine_NearZero()
    {
        // biên dọc (90°) cũng là "thẳng" -> straighten ~0 (mod 90).
        var img = TiltedHorizon(90f);
        float est = AutoStraighten.EstimateAngle(img);
        Assert.InRange(est, -2f, 2f);
    }
}
