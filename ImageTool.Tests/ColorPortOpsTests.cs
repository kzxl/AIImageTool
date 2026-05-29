using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ColorPortOpsTests
{
    private static LinearImage Solid(float r, float g, float b, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void WBKelvin_Identity_AtRef()
    {
        var op = new WhiteBalanceKelvinOp { Kelvin = 6500f, RefKelvin = 6500f, Tint = 0f };
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void WBKelvin_HigherKelvin_IsWarmer()
    {
        // Theo quy ước Lightroom: Kelvin cao hơn ref -> ảnh ấm hơn (R tăng so với B).
        var img = Solid(0.5f, 0.5f, 0.5f);
        new WhiteBalanceKelvinOp { Kelvin = 10000f, RefKelvin = 6500f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] > img.Pixels[2]); // R > B (ấm hơn)
    }

    [Fact]
    public void WBKelvin_LowerKelvin_IsCooler()
    {
        var img = Solid(0.5f, 0.5f, 0.5f);
        new WhiteBalanceKelvinOp { Kelvin = 3500f, RefKelvin = 6500f }.Apply(img, 1f);
        Assert.True(img.Pixels[2] > img.Pixels[0]); // B > R (lạnh hơn)
    }

    [Fact]
    public void WBKelvin_RoundTrip()
    {
        var op = new WhiteBalanceKelvinOp { Kelvin = 4200f, Tint = 0.3f };
        var back = WhiteBalanceKelvinOp.FromParams(op.ToParams());
        Assert.Equal(4200f, back.Kelvin, 1);
        Assert.Equal(0.3f, back.Tint, 4);
    }

    [Fact]
    public void SelectiveColor_Identity_WhenNoShift()
    {
        var op = new SelectiveColorOp { SourceHue = 0f, TargetHue = 0f };
        Assert.True(op.IsIdentity);
    }

    [Fact]
    public void SelectiveColor_ShiftsTargetHue()
    {
        // pixel đỏ (hue~0) -> dịch sang hue 120 (lục). Sau khi áp, G nên trội.
        var img = Solid(ColorSpace.SrgbToLinear(0.8f), ColorSpace.SrgbToLinear(0.1f), ColorSpace.SrgbToLinear(0.1f));
        new SelectiveColorOp { SourceHue = 0f, TargetHue = 120f, Tolerance = 40f, Strength = 1f }.Apply(img, 1f);
        Assert.True(img.Pixels[1] >= img.Pixels[0]); // G >= R sau khi dịch sang lục
    }

    [Fact]
    public void SelectiveColor_LeavesOutOfRangeHueAlone()
    {
        // pixel lam (hue~240), source=0 (đỏ), tolerance hẹp -> không đổi.
        var img = Solid(ColorSpace.SrgbToLinear(0.1f), ColorSpace.SrgbToLinear(0.1f), ColorSpace.SrgbToLinear(0.8f));
        float before = img.Pixels[2];
        new SelectiveColorOp { SourceHue = 0f, TargetHue = 120f, Tolerance = 20f, Strength = 1f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[2], before - 1e-3f, before + 1e-3f);
    }

    [Fact]
    public void LutCube_MissingFile_IsIdentity()
    {
        var op = new LutCubeOp { Path = "nonexistent.cube", Intensity = 1f };
        var img = Solid(0.5f, 0.4f, 0.3f);
        op.Apply(img, 1f); // không ném, không đổi
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void NewColorOps_Registered()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(WhiteBalanceKelvinOp.Type));
        Assert.True(reg.Has(SelectiveColorOp.Type));
        Assert.True(reg.Has(LutCubeOp.Type));
    }
}
