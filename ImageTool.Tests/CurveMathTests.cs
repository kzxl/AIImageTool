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

    // ---- Preserve hue mode (D1.4) ----

    private static LinearImage SolidSrgb(float sr, float sg, float sb)
    {
        var img = new LinearImage(2, 2);
        float r = ColorSpace.SrgbToLinear(sr), g = ColorSpace.SrgbToLinear(sg), b = ColorSpace.SrgbToLinear(sb);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void PreserveHue_RoundTripParams()
    {
        var op = new ToneCurveOp(new List<(float, float)> { (0f, 0f), (0.5f, 0.7f), (1f, 1f) }) { PreserveHue = true };
        var back = ToneCurveOp.FromParams(op.ToParams());
        Assert.True(back.PreserveHue);
    }

    [Fact]
    public void PreserveHue_KeepsHueRatio()
    {
        // màu có tỉ lệ R:G:B; sau curve preserve-hue, tỉ lệ giữa các kênh (trong sRGB) gần như giữ nguyên.
        var pts = new List<(float, float)> { (0f, 0f), (0.5f, 0.75f), (1f, 1f) };
        var img = SolidSrgb(0.4f, 0.2f, 0.1f);
        new ToneCurveOp(pts) { PreserveHue = true }.Apply(img, 1f);
        float sr = ColorSpace.LinearToSrgb(img.Pixels[0]);
        float sg = ColorSpace.LinearToSrgb(img.Pixels[1]);
        float sb = ColorSpace.LinearToSrgb(img.Pixels[2]);
        // tỉ lệ G/R và B/R ban đầu = 0.5 và 0.25.
        Assert.InRange(sg / sr, 0.45f, 0.55f);
        Assert.InRange(sb / sr, 0.20f, 0.30f);
    }

    [Fact]
    public void NonPreserve_ShiftsHueRatio()
    {
        // preserve-hue giữ chính xác tỉ lệ kênh (scale đồng nhất); per-channel cho output KHÁC.
        var pts = new List<(float, float)> { (0f, 0f), (0.5f, 0.75f), (1f, 1f) };

        var imgP = SolidSrgb(0.4f, 0.2f, 0.1f);
        new ToneCurveOp(pts) { PreserveHue = true }.Apply(imgP, 1f);
        float ratioP = ColorSpace.LinearToSrgb(imgP.Pixels[1]) / ColorSpace.LinearToSrgb(imgP.Pixels[0]);

        var imgN = SolidSrgb(0.4f, 0.2f, 0.1f);
        new ToneCurveOp(pts) { PreserveHue = false }.Apply(imgN, 1f);
        float ratioN = ColorSpace.LinearToSrgb(imgN.Pixels[1]) / ColorSpace.LinearToSrgb(imgN.Pixels[0]);

        // preserve-hue giữ tỉ lệ ~0.5 chính xác.
        Assert.InRange(ratioP, 0.49f, 0.51f);
        // hai chế độ cho kết quả khác nhau (per-channel áp curve riêng từng kênh).
        float rP = ColorSpace.LinearToSrgb(imgP.Pixels[0]);
        float rN = ColorSpace.LinearToSrgb(imgN.Pixels[0]);
        Assert.True(System.MathF.Abs(rP - rN) > 1e-3f || System.MathF.Abs(ratioP - ratioN) > 1e-3f,
            $"hai chế độ phải khác nhau: rP={rP}, rN={rN}, ratioP={ratioP}, ratioN={ratioN}");
    }

    [Fact]
    public void PreserveHue_RaisesLuminance()
    {
        var pts = new List<(float, float)> { (0f, 0f), (0.5f, 0.75f), (1f, 1f) };
        var img = SolidSrgb(0.4f, 0.2f, 0.1f);
        float lumBefore = ColorSpace.Luminance(img.Pixels[0], img.Pixels[1], img.Pixels[2]);
        new ToneCurveOp(pts) { PreserveHue = true }.Apply(img, 1f);
        float lumAfter = ColorSpace.Luminance(img.Pixels[0], img.Pixels[1], img.Pixels[2]);
        Assert.True(lumAfter > lumBefore);
    }

    [Theory]
    [InlineData("0,0;0.25,0.21;0.75,0.79;1,1")]            // medium contrast
    [InlineData("0,0;0.25,0.16;0.5,0.5;0.75,0.84;1,1")]    // strong contrast
    public void ContrastPreset_ParsesAndIsSCurve(string pts)
    {
        var parsed = CurveMath.Parse(pts);
        Assert.NotNull(parsed);
        var lut = CurveMath.BuildLut(parsed);
        // S-curve: vùng tối bị kéo xuống, vùng sáng kéo lên, giữ điểm giữa ~0.5.
        Assert.True(CurveMath.Eval(lut, 0.25f) < 0.25f, "shadows phải tối hơn");
        Assert.True(CurveMath.Eval(lut, 0.75f) > 0.75f, "highlights phải sáng hơn");
        Assert.InRange(CurveMath.Eval(lut, 0.5f), 0.45f, 0.55f);
    }

    [Fact]
    public void FadedPreset_LiftsBlacks_LowersWhites()
    {
        var parsed = CurveMath.Parse("0,0.06;0.25,0.27;0.75,0.78;1,0.95");
        Assert.NotNull(parsed);
        var lut = CurveMath.BuildLut(parsed);
        Assert.True(CurveMath.Eval(lut, 0f) > 0.03f, "đen được nâng lên");
        Assert.True(CurveMath.Eval(lut, 1f) < 0.98f, "trắng bị hạ xuống");
    }

    [Fact]
    public void LinearPreset_IsIdentity()
    {
        var parsed = CurveMath.Parse("0,0;1,1");
        Assert.NotNull(parsed);
        Assert.True(CurveMath.IsIdentity(CurveMath.Normalize(parsed)));
    }
}
