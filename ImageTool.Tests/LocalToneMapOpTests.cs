using System;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class LocalToneMapOpTests
{
    private static LinearImage Solid(int size, float v)
    {
        var img = new LinearImage(size, size);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Identity_WhenZeroAmountAndDetail()
    {
        Assert.True(new LocalToneMapOp { Amount = 0, Detail = 0 }.IsIdentity);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(LocalToneMapOp.Type));
    }

    [Fact]
    public void RoundTrip()
    {
        var back = LocalToneMapOp.FromParams(
            new LocalToneMapOp { Amount = 0.6f, Detail = 0.3f, BaseRadius = 50f }.ToParams());
        Assert.Equal(0.6f, back.Amount, 4);
        Assert.Equal(0.3f, back.Detail, 4);
        Assert.Equal(50f, back.BaseRadius, 4);
    }

    [Fact]
    public void Compresses_BrightTowardMidtone()
    {
        // ảnh sáng đều (lum > 0.18) -> nén base về midtone -> tối đi.
        var img = Solid(16, 0.85f);
        float before = img.Pixels[0];
        new LocalToneMapOp { Amount = 1f, Detail = 0f, BaseRadius = 30f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] < before, $"bright should darken: {before} -> {img.Pixels[0]}");
    }

    [Fact]
    public void Compresses_DarkTowardMidtone()
    {
        // ảnh tối đều (lum < 0.18) -> nén base về midtone -> sáng lên (mở bóng).
        var img = Solid(16, 0.03f);
        float before = img.Pixels[0];
        new LocalToneMapOp { Amount = 1f, Detail = 0f, BaseRadius = 30f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] > before, $"dark should lighten: {before} -> {img.Pixels[0]}");
    }

    [Fact]
    public void PreservesNeutralGray_HueStaysGray()
    {
        // pixel xám: sau tone map vẫn xám (R=G=B), vì scale RGB theo cùng gain.
        var img = Solid(16, 0.5f);
        new LocalToneMapOp { Amount = 0.8f, Detail = 0.2f, BaseRadius = 20f }.Apply(img, 1f);
        Assert.Equal(img.Pixels[0], img.Pixels[1], 5);
        Assert.Equal(img.Pixels[1], img.Pixels[2], 5);
    }

    [Fact]
    public void PreservesHue_OnColoredPixel()
    {
        // pixel màu: gain áp đều 3 kênh nên tỉ lệ R:G:B (hue) giữ nguyên.
        var img = new LinearImage(16, 16);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.6f; img.Pixels[i + 1] = 0.3f; img.Pixels[i + 2] = 0.15f; img.Pixels[i + 3] = 1f; }
        float ratioBefore = img.Pixels[0] / img.Pixels[1];
        new LocalToneMapOp { Amount = 0.7f, Detail = 0f, BaseRadius = 20f }.Apply(img, 1f);
        float ratioAfter = img.Pixels[0] / img.Pixels[1];
        Assert.Equal(ratioBefore, ratioAfter, 3);
    }

    [Fact]
    public void ReplaysViaPipeline()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var img = Solid(16, 0.8f);
        var ops = new System.Collections.Generic.List<EditOperation>
        {
            new EditOperation
            {
                OpType = LocalToneMapOp.Type,
                Params = new System.Collections.Generic.Dictionary<string, string>
                { ["amount"] = "1", ["detail"] = "0", ["radius"] = "30" }
            }
        };
        var result = pipeline.Render(img, ops);
        Assert.True(result.Pixels[0] < 0.8f); // nén sáng
    }
}
