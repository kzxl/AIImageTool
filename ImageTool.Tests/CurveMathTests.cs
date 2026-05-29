using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class CurveMathTests
{
    [Fact]
    public void Identity_IsDetected()
    {
        var pts = CurveMath.Normalize(new List<(float, float)> { (0f, 0f), (1f, 1f) });
        Assert.True(CurveMath.IsIdentity(pts));
    }

    [Fact]
    public void Identity_LutMapsInputToOutput()
    {
        var lut = CurveMath.BuildLut(new List<(float, float)> { (0f, 0f), (1f, 1f) });
        Assert.InRange(CurveMath.Eval(lut, 0.5f), 0.49f, 0.51f);
        Assert.InRange(CurveMath.Eval(lut, 0.0f), 0f, 0.01f);
        Assert.InRange(CurveMath.Eval(lut, 1.0f), 0.99f, 1f);
    }

    [Fact]
    public void Midpoint_Lift_RaisesOutput()
    {
        var lut = CurveMath.BuildLut(new List<(float, float)> { (0f, 0f), (0.5f, 0.75f), (1f, 1f) });
        Assert.InRange(CurveMath.Eval(lut, 0.5f), 0.70f, 0.80f);
    }

    [Fact]
    public void Monotone_NoOvershoot()
    {
        // điểm gây overshoot ở spline thường: kiểm tra LUT không vượt [0,1] và không giảm.
        var lut = CurveMath.BuildLut(new List<(float, float)> { (0f, 0f), (0.3f, 0.8f), (0.7f, 0.2f), (1f, 1f) });
        foreach (var v in lut) Assert.InRange(v, 0f, 1f);
    }

    [Fact]
    public void Serialize_Parse_RoundTrip()
    {
        var pts = new List<(float, float)> { (0f, 0.1f), (0.5f, 0.6f), (1f, 0.9f) };
        var s = CurveMath.Serialize(pts);
        var back = CurveMath.Parse(s);
        Assert.NotNull(back);
        Assert.Equal(3, back!.Count);
        Assert.Equal(0.5f, back[1].x, 4);
        Assert.Equal(0.6f, back[1].y, 4);
    }

    [Fact]
    public void Parse_InvalidReturnsNull()
    {
        Assert.Null(CurveMath.Parse(""));
        Assert.Null(CurveMath.Parse("0,0")); // chỉ 1 điểm
    }

    [Fact]
    public void CurveMath_MatchesToneCurveOp()
    {
        // CurveMath và ToneCurveOp phải cho cùng kết quả tại midpoint.
        var pts = new List<(float, float)> { (0f, 0f), (0.5f, 0.7f), (1f, 1f) };
        var lut = CurveMath.BuildLut(pts);
        float expected = CurveMath.Eval(lut, 0.5f);

        var img = new LinearImage(2, 2);
        float lin = ColorSpace.SrgbToLinear(0.5f);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = lin; img.Pixels[i + 1] = lin; img.Pixels[i + 2] = lin; img.Pixels[i + 3] = 1f; }
        new ToneCurveOp(pts).Apply(img, 1f);
        float opResult = ColorSpace.LinearToSrgb(img.Pixels[0]);
        Assert.InRange(opResult, expected - 0.02f, expected + 0.02f);
    }
}
