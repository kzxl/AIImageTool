using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ColorSpacesTests
{
    private static void AssertMatrixClose(float[] a, float[] expectedIdentity)
    {
        for (int i = 0; i < 9; i++)
            Assert.Equal(expectedIdentity[i], a[i], 3);
    }

    [Fact]
    public void ConversionMatrix_SameSpace_IsIdentity()
    {
        var m = ColorSpaces.ConversionMatrix(ColorSpaces.Space.AdobeRgb, ColorSpaces.Space.AdobeRgb);
        AssertMatrixClose(m, new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });
    }

    [Fact]
    public void Invert3x3_RoundTrips()
    {
        var m = new float[] { 0.5767309f, 0.1855540f, 0.1881852f, 0.2973769f, 0.6273491f, 0.0752741f, 0.0270343f, 0.0706872f, 0.9911085f };
        var inv = ColorSpaces.Invert3x3(m);
        var prod = ColorSpaces.Mul3x3(m, inv);
        AssertMatrixClose(prod, new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });
    }

    [Fact]
    public void AdobeToSrgb_ThenSrgbToAdobe_IsIdentity()
    {
        var toSrgb = ColorSpaces.ConversionMatrix(ColorSpaces.Space.AdobeRgb, ColorSpaces.Space.Srgb);
        var back = ColorSpaces.ConversionMatrix(ColorSpaces.Space.Srgb, ColorSpaces.Space.AdobeRgb);
        var prod = ColorSpaces.Mul3x3(back, toSrgb);
        AssertMatrixClose(prod, new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });
    }

    [Fact]
    public void WideGamut_PrimaryShrinksWhenMappedToSrgb()
    {
        // Đỏ thuần Rec2020 -> trong sRGB linear sẽ có thành phần G/B âm (ngoài gamut) và R giảm.
        // Kiểm tra: kênh G của kết quả < 0 trước clamp (gamut rộng hơn sRGB).
        var m = ColorSpaces.ToWorkingMatrix(ColorSpaces.Space.Rec2020);
        float r = 1f, g = 0f, b = 0f;
        float ng = m[3] * r + m[4] * g + m[5] * b;
        Assert.True(ng < 0f, "Rec2020 đỏ thuần phải có G âm khi chuyển sang sRGB");
    }

    [Fact]
    public void TryParse_Works()
    {
        Assert.True(ColorSpaces.TryParse("AdobeRGB", out var s1)); Assert.Equal(ColorSpaces.Space.AdobeRgb, s1);
        Assert.True(ColorSpaces.TryParse("rec2020", out var s2)); Assert.Equal(ColorSpaces.Space.Rec2020, s2);
        Assert.True(ColorSpaces.TryParse("p3", out var s3)); Assert.Equal(ColorSpaces.Space.DisplayP3, s3);
        Assert.True(ColorSpaces.TryParse("", out var s4)); Assert.Equal(ColorSpaces.Space.Srgb, s4);
        Assert.False(ColorSpaces.TryParse("weird", out var s5)); Assert.Equal(ColorSpaces.Space.Srgb, s5);
    }
}

public class InputProfileOpTests
{
    private static LinearImage Solid(float r, float g, float b)
    {
        var img = new LinearImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Identity_WhenSrgb()
    {
        Assert.True(new InputProfileOp { Source = ColorSpaces.Space.Srgb }.IsIdentity);
    }

    [Fact]
    public void GrayStaysGray_AdobeRgb()
    {
        // Xám trung tính (cùng white point D65) phải gần như không đổi qua chuyển gamut.
        var img = Solid(0.5f, 0.5f, 0.5f);
        new InputProfileOp { Source = ColorSpaces.Space.AdobeRgb }.Apply(img, 1f);
        Assert.Equal(0.5f, img.Pixels[0], 2);
        Assert.Equal(0.5f, img.Pixels[1], 2);
        Assert.Equal(0.5f, img.Pixels[2], 2);
    }

    [Fact]
    public void Rec2020Green_DesaturatesIntoSrgb()
    {
        // Lục thuần Rec2020 -> trong sRGB, kênh R/B clamp về 0, kênh G vẫn trội.
        var img = Solid(0f, 1f, 0f);
        new InputProfileOp { Source = ColorSpaces.Space.Rec2020 }.Apply(img, 1f);
        Assert.True(img.Pixels[1] > img.Pixels[0]);
        Assert.True(img.Pixels[1] > img.Pixels[2]);
        Assert.True(img.Pixels[0] >= 0f && img.Pixels[2] >= 0f); // clamp không âm
    }

    [Fact]
    public void RoundTrip_ParamsPreserveSpace()
    {
        var back = InputProfileOp.FromParams(new InputProfileOp { Source = ColorSpaces.Space.DisplayP3 }.ToParams());
        Assert.Equal(ColorSpaces.Space.DisplayP3, back.Source);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(InputProfileOp.Type));
    }

    [Fact]
    public void CustomMatrix_OverridesSpace_NotIdentity()
    {
        // SourceMatrix != null -> không còn identity dù Source = sRGB.
        var op = new InputProfileOp { Source = ColorSpaces.Space.Srgb, SourceMatrix = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.AdobeRgb) };
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void CustomMatrix_SrgbMatrix_IsNoop()
    {
        // Ma trận nguồn = sRGB -> (XYZ->sRGB)*(sRGB->XYZ) = identity -> ảnh không đổi.
        var img = Solid(0.3f, 0.6f, 0.2f);
        var before = (float[])img.Pixels.Clone();
        new InputProfileOp { SourceMatrix = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.Srgb) }.Apply(img, 1f);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], img.Pixels[i], 4);
    }

    [Fact]
    public void CustomMatrix_MatchesEquivalentSpace()
    {
        // Dùng SourceMatrix = ma trận AdobeRGB phải cho KẾT QUẢ GIỐNG Source = AdobeRGB.
        var imgA = Solid(0.7f, 0.4f, 0.2f);
        var imgB = Solid(0.7f, 0.4f, 0.2f);
        new InputProfileOp { Source = ColorSpaces.Space.AdobeRgb }.Apply(imgA, 1f);
        new InputProfileOp { SourceMatrix = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.AdobeRgb) }.Apply(imgB, 1f);
        for (int i = 0; i < imgA.Pixels.Length; i++)
            Assert.Equal(imgA.Pixels[i], imgB.Pixels[i], 4);
    }

    [Fact]
    public void CustomMatrix_RoundTrip()
    {
        var src = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.Rec2020);
        var back = InputProfileOp.FromParams(new InputProfileOp { SourceMatrix = src }.ToParams());
        Assert.NotNull(back.SourceMatrix);
        Assert.Equal(9, back.SourceMatrix!.Length);
        for (int i = 0; i < 9; i++) Assert.Equal(src[i], back.SourceMatrix[i], 4);
    }
}
