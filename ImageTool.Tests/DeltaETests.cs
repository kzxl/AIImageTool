using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class DeltaETests
{
    [Fact]
    public void Cie76_SameColor_Zero()
    {
        var a = new DeltaE.Lab(50, 10, -20);
        Assert.Equal(0f, DeltaE.Cie76(a, a), 4);
    }

    [Fact]
    public void Cie76_KnownDistance()
    {
        var a = new DeltaE.Lab(50, 0, 0);
        var b = new DeltaE.Lab(53, 4, 0); // dL=3, da=4 -> 5
        Assert.Equal(5f, DeltaE.Cie76(a, b), 3);
    }

    [Fact]
    public void Ciede2000_SameColor_Zero()
    {
        var a = new DeltaE.Lab(63, 20, 30);
        Assert.Equal(0f, DeltaE.Ciede2000(a, a), 4);
    }

    // Test vector chuẩn từ Sharma, Wu, Dalal (2005) - bảng kiểm CIEDE2000.
    [Theory]
    [InlineData(50.0000, 2.6772, -79.7751, 50.0000, 0.0000, -82.7485, 2.0425)]
    [InlineData(50.0000, 3.1571, -77.2803, 50.0000, 0.0000, -82.7485, 2.8615)]
    [InlineData(50.0000, 2.8361, -74.0200, 50.0000, 0.0000, -82.7485, 3.4412)]
    [InlineData(50.0000, -1.3802, -84.2814, 50.0000, 0.0000, -82.7485, 1.0000)]
    [InlineData(50.0000, 0.0000, 0.0000, 50.0000, -1.0000, 2.0000, 2.3669)]
    [InlineData(50.0000, 2.5000, 0.0000, 73.0000, 25.0000, -18.0000, 27.1492)]
    [InlineData(50.0000, 2.5000, 0.0000, 50.0000, 3.1736, 0.5854, 1.0000)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    public void Ciede2000_MatchesSharmaTestVectors(
        double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        var p1 = new DeltaE.Lab((float)l1, (float)a1, (float)b1);
        var p2 = new DeltaE.Lab((float)l2, (float)a2, (float)b2);
        float de = DeltaE.Ciede2000(p1, p2);
        Assert.Equal(expected, de, 3); // sai số < 0.001
    }

    [Fact]
    public void FromSrgb8_IdenticalColors_ZeroDelta()
    {
        var a = DeltaE.FromSrgb8(128, 64, 200);
        var b = DeltaE.FromSrgb8(128, 64, 200);
        Assert.Equal(0f, DeltaE.Ciede2000(a, b), 4);
    }

    [Fact]
    public void FromSrgb8_SlightDifference_SmallDelta()
    {
        var a = DeltaE.FromSrgb8(128, 128, 128);
        var b = DeltaE.FromSrgb8(130, 128, 128); // lệch nhẹ
        float de = DeltaE.Ciede2000(a, b);
        Assert.True(de > 0f && de < 2f, $"ΔE={de} nên nhỏ");
    }

    [Fact]
    public void FromSrgb8_BlackVsWhite_LargeDelta()
    {
        var black = DeltaE.FromSrgb8(0, 0, 0);
        var white = DeltaE.FromSrgb8(255, 255, 255);
        Assert.True(DeltaE.Ciede2000(black, white) > 90f);
    }
}
