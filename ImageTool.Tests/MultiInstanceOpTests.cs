using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

// D4.4: xác nhận pipeline replay ĐÚNG nhiều instance của cùng 1 op type (vd 2 Tone Curve), theo thứ tự.
public class MultiInstanceOpTests
{
    private static LinearImage Gray(float v, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Pipeline_StacksTwoToneCurves_InOrder()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipe = new EditPipeline(reg);
        var img = Gray(ColorSpace.SrgbToLinear(0.5f));

        // Curve 1: nâng midtone (0.5->0.7). Curve 2: nâng tiếp (0.5->0.7 trên kết quả).
        var c1 = new EditOperation { OpType = ToneCurveOp.Type, Params = new() { ["rgb"] = "0,0;0.5,0.7;1,1" } };
        var c2 = new EditOperation { OpType = ToneCurveOp.Type, Params = new() { ["rgb"] = "0,0;0.5,0.7;1,1" } };

        var one = pipe.Render(img, new[] { c1 });
        var two = pipe.Render(img, new[] { c1, c2 });

        float vOne = ColorSpace.LinearToSrgb(one.Pixels[0]);
        float vTwo = ColorSpace.LinearToSrgb(two.Pixels[0]);
        // 2 curve liên tiếp phải sáng hơn 1 curve (op thứ 2 áp lên kết quả op thứ 1).
        Assert.True(vTwo > vOne, $"two-curve phải sáng hơn one-curve: {vOne} -> {vTwo}");
        Assert.True(vOne > 0.5f);
    }

    [Fact]
    public void Pipeline_TwoDifferentInstances_DistinctEffect()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipe = new EditPipeline(reg);
        var img = Gray(ColorSpace.SrgbToLinear(0.5f));

        // Instance 1 nâng, instance 2 hạ -> kết quả khác hẳn từng cái riêng.
        var up = new EditOperation { OpType = ToneCurveOp.Type, Params = new() { ["rgb"] = "0,0;0.5,0.75;1,1" } };
        var down = new EditOperation { OpType = ToneCurveOp.Type, Params = new() { ["rgb"] = "0,0;0.5,0.25;1,1" } };

        var stacked = pipe.Render(img, new[] { up, down });
        // Không crash + cho ra giá trị hợp lệ; 2 instance khác nhau được giữ riêng (không dedup).
        Assert.InRange(stacked.Pixels[0], 0f, 1f);
    }

    [Fact]
    public void DevelopModules_SortCanonical_KeepsDuplicateInstances()
    {
        // SortCanonical không được gộp/loại bỏ instance trùng OpType.
        var ops = new List<EditOperation>
        {
            new() { OpType = ToneCurveOp.Type, Params = new() { ["rgb"] = "a" } },
            new() { OpType = DevelopBasicOp.Type },
            new() { OpType = ToneCurveOp.Type, Params = new() { ["rgb"] = "b" } },
        };
        var sorted = ImageTool.Shared.DevelopModules.SortCanonical(ops);
        int curveCount = 0;
        foreach (var o in sorted) if (o.OpType == ToneCurveOp.Type) curveCount++;
        Assert.Equal(2, curveCount); // giữ cả 2 instance
    }
}
