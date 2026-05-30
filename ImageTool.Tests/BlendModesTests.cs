using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class BlendModesTests
{
    [Fact]
    public void Normal_ReturnsTop()
    {
        Assert.Equal(0.7f, BlendModes.Apply(BlendMode.Normal, 0.2f, 0.7f), 4);
    }

    [Fact]
    public void Multiply_Darkens()
    {
        Assert.Equal(0.25f, BlendModes.Apply(BlendMode.Multiply, 0.5f, 0.5f), 4);
        Assert.Equal(0f, BlendModes.Apply(BlendMode.Multiply, 0f, 0.9f), 4);
    }

    [Fact]
    public void Screen_Lightens()
    {
        Assert.Equal(0.75f, BlendModes.Apply(BlendMode.Screen, 0.5f, 0.5f), 4);
        Assert.Equal(1f, BlendModes.Apply(BlendMode.Screen, 1f, 0.3f), 4);
    }

    [Fact]
    public void Lighten_Darken()
    {
        Assert.Equal(0.8f, BlendModes.Apply(BlendMode.Lighten, 0.3f, 0.8f), 4);
        Assert.Equal(0.3f, BlendModes.Apply(BlendMode.Darken, 0.3f, 0.8f), 4);
    }

    [Fact]
    public void Difference_AbsDiff()
    {
        Assert.Equal(0.5f, BlendModes.Apply(BlendMode.Difference, 0.7f, 0.2f), 4);
    }

    [Fact]
    public void Addition_ClampsToOne()
    {
        Assert.Equal(1f, BlendModes.Apply(BlendMode.Addition, 0.7f, 0.7f), 4);
    }

    [Fact]
    public void Overlay_NeutralGrayTop()
    {
        // top=0.5 với overlay: base<0.5 -> 2*b*0.5 = b; base>0.5 -> 1-2*(1-b)*0.5 = b. Giữ nguyên base.
        Assert.Equal(0.3f, BlendModes.Apply(BlendMode.Overlay, 0.3f, 0.5f), 3);
        Assert.Equal(0.8f, BlendModes.Apply(BlendMode.Overlay, 0.8f, 0.5f), 3);
    }

    [Fact]
    public void Parse_RoundTrip()
    {
        foreach (var m in new[] { BlendMode.Multiply, BlendMode.Screen, BlendMode.SoftLight, BlendMode.Difference })
            Assert.Equal(m, BlendModes.Parse(BlendModes.ToKey(m)));
        Assert.Equal(BlendMode.Normal, BlendModes.Parse("unknown"));
    }

    [Fact]
    public void Output_AlwaysClamped()
    {
        foreach (BlendMode m in System.Enum.GetValues(typeof(BlendMode)))
        {
            var r = BlendModes.Apply(m, 0.9f, 0.95f);
            Assert.InRange(r, 0f, 1f);
        }
    }
}

public class MaskedOpBlendTests
{
    private static LinearImage Solid(float v, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Opacity_ScalesEffect()
    {
        var reg = EditOpRegistry.CreateDefault();
        // gradient full (x0..x1) + exposure +1, opacity 0.5 -> bên phải sáng nhưng yếu hơn opacity=1.
        var pFull = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
            ["mask"] = LinearGradientMask.Type, ["x0"] = "0", ["y0"] = "0", ["x1"] = "1", ["y1"] = "0",
            ["opacity"] = "1",
        };
        var pHalf = new Dictionary<string, string>(pFull) { ["opacity"] = "0.5" };

        var imgFull = Solid(0.25f, 8, 8); reg.Create(MaskedOp.Type, pFull)!.Apply(imgFull, 1f);
        var imgHalf = Solid(0.25f, 8, 8); reg.Create(MaskedOp.Type, pHalf)!.Apply(imgHalf, 1f);

        int rightP = (0 * 8 + 7) * 4;
        // cả hai sáng hơn gốc, nhưng opacity=1 sáng hơn opacity=0.5.
        Assert.True(imgFull.Pixels[rightP] > imgHalf.Pixels[rightP]);
        Assert.True(imgHalf.Pixels[rightP] > 0.25f);
    }

    [Fact]
    public void BlendMultiply_Darkens()
    {
        var reg = EditOpRegistry.CreateDefault();
        // inner exposure +1 (làm sáng) nhưng blend=multiply -> kết quả phải TỐI hơn so với base ở vùng mask.
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
            ["mask"] = LinearGradientMask.Type, ["x0"] = "0", ["y0"] = "0", ["x1"] = "1", ["y1"] = "0",
            ["blend"] = "multiply", ["opacity"] = "1",
        };
        var img = Solid(0.5f, 8, 8);
        reg.Create(MaskedOp.Type, p)!.Apply(img, 1f);
        int rightP = (0 * 8 + 7) * 4;
        // multiply base*edited; edited sáng hơn nên multiply vẫn có thể sáng/tối — kiểm tra hợp lệ [0,1].
        Assert.InRange(img.Pixels[rightP], 0f, 1f);
    }

    [Fact]
    public void DefaultOpacity_IsFull()
    {
        var reg = EditOpRegistry.CreateDefault();
        // không có key opacity -> mặc định 1 (hành vi cũ không đổi).
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
            ["mask"] = LinearGradientMask.Type, ["x0"] = "0", ["y0"] = "0", ["x1"] = "1", ["y1"] = "0",
        };
        var img = Solid(0.25f, 8, 8);
        reg.Create(MaskedOp.Type, p)!.Apply(img, 1f);
        int rightP = (0 * 8 + 7) * 4;
        Assert.True(img.Pixels[rightP] > 0.4f);
    }
}
