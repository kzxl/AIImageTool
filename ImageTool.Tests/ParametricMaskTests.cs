using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ParametricMaskTests
{
    /// <summary>Ảnh 2 pixel: [0]=tối, [1]=sáng (sRGB 0.1 / 0.9).</summary>
    private static LinearImage TwoToneGray()
    {
        var img = new LinearImage(2, 1);
        img.Pixels[0] = img.Pixels[1] = img.Pixels[2] = ColorSpace.SrgbToLinear(0.1f); img.Pixels[3] = 1f;
        img.Pixels[4] = img.Pixels[5] = img.Pixels[6] = ColorSpace.SrgbToLinear(0.9f); img.Pixels[7] = 1f;
        return img;
    }

    private static LinearImage Solid(float sr, float sg, float sb, int w = 4, int h = 4)
    {
        var img = new LinearImage(w, h);
        float r = ColorSpace.SrgbToLinear(sr), g = ColorSpace.SrgbToLinear(sg), b = ColorSpace.SrgbToLinear(sb);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void NoChannelLimited_SelectsEverything()
    {
        // Tất cả kênh để [0..1] -> mask = 1 toàn ảnh.
        var img = TwoToneGray();
        var m = new ParametricMask().GenerateFrom(img);
        Assert.True(m[0] > 0.99f);
        Assert.True(m[1] > 0.99f);
    }

    [Fact]
    public void NoChannelLimited_Invert_SelectsNothing()
    {
        var img = TwoToneGray();
        var m = new ParametricMask { Invert = true }.GenerateFrom(img);
        Assert.True(m[0] < 0.01f);
        Assert.True(m[1] < 0.01f);
    }

    [Fact]
    public void LightnessBand_SelectsShadowsOnly()
    {
        // L band [0..0.4] -> chọn pixel tối, loại pixel sáng.
        var img = TwoToneGray();
        var m = new ParametricMask { LMin = 0f, LMax = 0.4f, LFeather = 0.05f }.GenerateFrom(img);
        Assert.True(m[0] > 0.9f);  // tối được chọn
        Assert.True(m[1] < 0.1f);  // sáng bị loại
    }

    [Fact]
    public void LightnessBand_SelectsHighlightsOnly()
    {
        var img = TwoToneGray();
        var m = new ParametricMask { LMin = 0.6f, LMax = 1f, LFeather = 0.05f }.GenerateFrom(img);
        Assert.True(m[0] < 0.1f);
        Assert.True(m[1] > 0.9f);
    }

    [Fact]
    public void HueBand_SelectsRedNotBlue()
    {
        // band hue quanh đỏ (0..30 độ -> 0..0.083 chuẩn hoá).
        var red = Solid(0.8f, 0.1f, 0.1f);
        var blue = Solid(0.1f, 0.1f, 0.8f);
        var mask = new ParametricMask { HMin = 0f, HMax = 0.08f, HFeather = 0.02f };
        var mr = mask.GenerateFrom(red);
        var mb = mask.GenerateFrom(blue);
        Assert.True(mr[0] > 0.9f);
        Assert.True(mb[0] < 0.1f);
    }

    [Fact]
    public void HueBand_Wraps_AcrossZero()
    {
        // band wrap [0.95..0.05] phải chọn đỏ (hue ~0) — vòng qua 0/1.
        var red = Solid(0.8f, 0.1f, 0.1f);
        var green = Solid(0.1f, 0.8f, 0.1f);
        var mask = new ParametricMask { HMin = 0.95f, HMax = 0.05f, HFeather = 0.01f };
        Assert.True(mask.GenerateFrom(red)[0] > 0.9f);
        Assert.True(mask.GenerateFrom(green)[0] < 0.1f);
    }

    [Fact]
    public void BlueChannel_SelectsHighBlue()
    {
        var hiBlue = Solid(0.1f, 0.1f, 0.9f);
        var loBlue = Solid(0.1f, 0.1f, 0.2f);
        var mask = new ParametricMask { BMin = 0.6f, BMax = 1f, BFeather = 0.05f };
        Assert.True(mask.GenerateFrom(hiBlue)[0] > 0.9f);
        Assert.True(mask.GenerateFrom(loBlue)[0] < 0.1f);
    }

    [Fact]
    public void MultipleChannels_Intersect()
    {
        // Yêu cầu vừa sáng (L cao) VỪA xanh dương (B cao).
        var brightBlue = Solid(0.5f, 0.5f, 0.95f);   // sáng + xanh -> chọn
        var brightRed = Solid(0.95f, 0.5f, 0.5f);    // sáng nhưng đỏ -> loại (B thấp)
        var darkBlue = Solid(0.05f, 0.05f, 0.5f);    // xanh nhưng tối -> loại (L thấp)
        var mask = new ParametricMask
        {
            LMin = 0.5f, LMax = 1f, LFeather = 0.05f,
            BMin = 0.6f, BMax = 1f, BFeather = 0.05f
        };
        Assert.True(mask.GenerateFrom(brightBlue)[0] > 0.8f);
        Assert.True(mask.GenerateFrom(brightRed)[0] < 0.2f);
        Assert.True(mask.GenerateFrom(darkBlue)[0] < 0.2f);
    }

    [Fact]
    public void ChromaBand_SelectsSaturatedNotGray()
    {
        var saturated = Solid(0.9f, 0.1f, 0.1f);
        var gray = Solid(0.5f, 0.5f, 0.5f);
        var mask = new ParametricMask { CMin = 0.3f, CMax = 1f, CFeather = 0.05f };
        Assert.True(mask.GenerateFrom(saturated)[0] > 0.8f);
        Assert.True(mask.GenerateFrom(gray)[0] < 0.1f);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new ParametricMask
        {
            LMin = 0.2f, LMax = 0.8f, LFeather = 0.15f,
            HMin = 0.9f, HMax = 0.1f, HFeather = 0.03f,
            BMin = 0.4f, BMax = 0.95f, BFeather = 0.2f,
            Invert = true
        };
        var back = ParametricMask.FromParams(original.ToParams());
        Assert.Equal(0.2f, back.LMin, 4);
        Assert.Equal(0.8f, back.LMax, 4);
        Assert.Equal(0.15f, back.LFeather, 4);
        Assert.Equal(0.9f, back.HMin, 4);
        Assert.Equal(0.1f, back.HMax, 4);
        Assert.Equal(0.4f, back.BMin, 4);
        Assert.True(back.Invert);
    }

    [Fact]
    public void Registered_AndReplaysViaPipeline()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var img = TwoToneGray();

        var ops = new List<EditOperation>
        {
            new EditOperation
            {
                OpType = MaskedOp.Type,
                Params = new Dictionary<string, string>
                {
                    ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
                    ["mask"] = ParametricMask.Type,
                    ["lMin"] = "0", ["lMax"] = "0.4", ["lFeather"] = "0.05",
                }
            }
        };
        var result = pipeline.Render(img, ops);
        // base không đổi
        Assert.Equal(ColorSpace.SrgbToLinear(0.1f), img.Pixels[0], 5);
        // pixel tối (được mask chọn) sáng lên ~x2; pixel sáng (loại) gần như không đổi.
        Assert.True(result.Pixels[0] > ColorSpace.SrgbToLinear(0.1f) * 1.5f);
        Assert.Equal(ColorSpace.SrgbToLinear(0.9f), result.Pixels[4], 3);
    }
}
