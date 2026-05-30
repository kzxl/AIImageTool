using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class MaskCombineTests
{
    [Fact]
    public void Parse_Works()
    {
        Assert.Equal(MaskCombineMode.Intersect, MaskCombine.Parse("intersect"));
        Assert.Equal(MaskCombineMode.Union, MaskCombine.Parse("union"));
        Assert.Equal(MaskCombineMode.Subtract, MaskCombine.Parse("subtract"));
        Assert.Equal(MaskCombineMode.None, MaskCombine.Parse(""));
        Assert.Equal(MaskCombineMode.None, MaskCombine.Parse("xyz"));
    }

    [Fact]
    public void Intersect_MultipliesMasks()
    {
        var a = new[] { 1f, 1f, 0.5f, 0f };
        var b = new[] { 0.5f, 0f, 1f, 1f };
        MaskCombine.Apply(a, b, MaskCombineMode.Intersect);
        Assert.Equal(0.5f, a[0], 4);
        Assert.Equal(0f, a[1], 4);
        Assert.Equal(0.5f, a[2], 4);
        Assert.Equal(0f, a[3], 4);
    }

    [Fact]
    public void Union_CombinesMasks()
    {
        var a = new[] { 0.5f, 1f, 0f };
        var b = new[] { 0.5f, 0f, 0f };
        MaskCombine.Apply(a, b, MaskCombineMode.Union);
        Assert.Equal(0.75f, a[0], 4); // 0.5+0.5-0.25
        Assert.Equal(1f, a[1], 4);
        Assert.Equal(0f, a[2], 4);
    }

    [Fact]
    public void Subtract_RemovesSecondary()
    {
        var a = new[] { 1f, 1f, 0.8f };
        var b = new[] { 1f, 0f, 0.5f };
        MaskCombine.Apply(a, b, MaskCombineMode.Subtract);
        Assert.Equal(0f, a[0], 4);   // 1*(1-1)
        Assert.Equal(1f, a[1], 4);   // 1*(1-0)
        Assert.Equal(0.4f, a[2], 4); // 0.8*(1-0.5)
    }

    [Fact]
    public void None_LeavesUnchanged()
    {
        var a = new[] { 0.3f, 0.7f };
        var orig = (float[])a.Clone();
        MaskCombine.Apply(a, new[] { 1f, 1f }, MaskCombineMode.None);
        Assert.Equal(orig[0], a[0], 5);
        Assert.Equal(orig[1], a[1], 5);
    }

    private static LinearImage TwoTone()
    {
        // pixel 0 tối (sRGB 0.1), pixel 1 sáng (sRGB 0.9).
        var img = new LinearImage(2, 1);
        img.Pixels[0] = img.Pixels[1] = img.Pixels[2] = ColorSpace.SrgbToLinear(0.1f); img.Pixels[3] = 1f;
        img.Pixels[4] = img.Pixels[5] = img.Pixels[6] = ColorSpace.SrgbToLinear(0.9f); img.Pixels[7] = 1f;
        return img;
    }

    [Fact]
    public void MaskedOp_RefineIntersect_RestrictsToBrightRegion()
    {
        // mask chính = toàn ảnh (radial invert lớn) hoặc gradient phủ hết; dùng gradient hết = 1.
        // Đơn giản: dùng LinearGradient phủ đều bằng cách x0=x1 (len2~0 -> fill 1).
        var reg = EditOpRegistry.CreateDefault();
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
            ["mask"] = LinearGradientMask.Type,
            ["x0"] = "0", ["y0"] = "0", ["x1"] = "0", ["y1"] = "0", // len2=0 -> mask fill 1
            // refine: chỉ giữ vùng SÁNG (luminance > 0.7).
            ["combine"] = "intersect", ["c_min"] = "0.7", ["c_max"] = "1", ["c_smooth"] = "0.05",
        };
        var op = reg.Create(MaskedOp.Type, p)!;
        var img = TwoTone();
        op.Apply(img, 1f);

        // pixel tối (bị refine loại) gần như không đổi; pixel sáng được nâng +1EV.
        Assert.Equal(ColorSpace.SrgbToLinear(0.1f), img.Pixels[0], 3);
        Assert.True(img.Pixels[4] > ColorSpace.SrgbToLinear(0.9f) * 1.3f);
    }

    [Fact]
    public void MaskedOp_RefineSubtract_RemovesBrightRegion()
    {
        var reg = EditOpRegistry.CreateDefault();
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
            ["mask"] = LinearGradientMask.Type,
            ["x0"] = "0", ["y0"] = "0", ["x1"] = "0", ["y1"] = "0",
            ["combine"] = "subtract", ["c_min"] = "0.7", ["c_max"] = "1", ["c_smooth"] = "0.05",
        };
        var op = reg.Create(MaskedOp.Type, p)!;
        var img = TwoTone();
        op.Apply(img, 1f);

        // subtract vùng sáng -> pixel sáng giữ nguyên, pixel tối được nâng.
        Assert.True(img.Pixels[0] > ColorSpace.SrgbToLinear(0.1f) * 1.3f);
        Assert.Equal(ColorSpace.SrgbToLinear(0.9f), img.Pixels[4], 3);
    }
}
