using System.Collections.Generic;
using System.Linq;
using ImageTool.Core;
using ImageTool.Imaging;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class DevelopModulesTests
{
    private static EditOperation Op(string opType, string val = "1")
        => new() { PluginId = "Develop", OpType = opType, Params = new Dictionary<string, string> { ["v"] = val } };

    [Fact]
    public void ModuleOf_MapsKnownTypes()
    {
        Assert.Equal("basic", DevelopModules.ModuleKeyOf(DevelopBasicOp.Type));
        Assert.Equal("detail", DevelopModules.ModuleKeyOf(SharpenOp.Type));
        Assert.Equal("detail", DevelopModules.ModuleKeyOf(HotPixelOp.Type));
        Assert.Equal("colormix", DevelopModules.ModuleKeyOf(HslMixerOp.Type));
        Assert.Equal("local", DevelopModules.ModuleKeyOf(MaskedOp.Type));
    }

    [Fact]
    public void ModuleOf_UnknownReturnsEmpty()
    {
        Assert.Equal("", DevelopModules.ModuleKeyOf("NoSuchOp"));
    }

    [Fact]
    public void CanonicalIndex_OrdersGeometryBeforeColor()
    {
        Assert.True(DevelopModules.CanonicalIndex(CropOp.Type) < DevelopModules.CanonicalIndex(DevelopBasicOp.Type));
        Assert.True(DevelopModules.CanonicalIndex(DevelopBasicOp.Type) < DevelopModules.CanonicalIndex(HslMixerOp.Type));
        Assert.True(DevelopModules.CanonicalIndex(SharpenOp.Type) < DevelopModules.CanonicalIndex(MaskedOp.Type));
    }

    [Fact]
    public void SortCanonical_ReordersToPipelineOrder()
    {
        var ops = new List<EditOperation> { Op(SharpenOp.Type), Op(CropOp.Type), Op(DevelopBasicOp.Type) };
        var sorted = DevelopModules.SortCanonical(ops);
        Assert.Equal(CropOp.Type, sorted[0].OpType);
        Assert.Equal(DevelopBasicOp.Type, sorted[1].OpType);
        Assert.Equal(SharpenOp.Type, sorted[2].OpType);
    }

    [Fact]
    public void ModulesPresent_ReportsOnlyExisting()
    {
        var ops = new List<EditOperation> { Op(DevelopBasicOp.Type), Op(SharpenOp.Type) };
        var mods = DevelopModules.ModulesPresent(ops);
        var keys = mods.Select(m => m.Key).ToList();
        Assert.Contains("basic", keys);
        Assert.Contains("detail", keys);
        Assert.DoesNotContain("colormix", keys);
    }

    [Fact]
    public void SelectivePaste_OnlyReplacesSelectedModule()
    {
        // Đích: Basic(target) + HSL(target). Nguồn: Basic(source) + Sharpen(source).
        var target = new List<EditOperation> { Op(DevelopBasicOp.Type, "T"), Op(HslMixerOp.Type, "T") };
        var source = new List<EditOperation> { Op(DevelopBasicOp.Type, "S"), Op(SharpenOp.Type, "S") };
        var sel = new HashSet<string> { "basic" };

        var merged = DevelopModules.SelectivePaste(target, source, sel);

        // Basic phải đến từ nguồn (S), HSL giữ của đích (T), Sharpen của nguồn KHÔNG được thêm (module detail không chọn).
        var basic = merged.Single(o => o.OpType == DevelopBasicOp.Type);
        Assert.Equal("S", basic.Params["v"]);
        var hsl = merged.Single(o => o.OpType == HslMixerOp.Type);
        Assert.Equal("T", hsl.Params["v"]);
        Assert.DoesNotContain(merged, o => o.OpType == SharpenOp.Type);
    }

    [Fact]
    public void SelectivePaste_AddsModuleAbsentInTarget()
    {
        var target = new List<EditOperation> { Op(DevelopBasicOp.Type, "T") };
        var source = new List<EditOperation> { Op(SharpenOp.Type, "S") };
        var sel = new HashSet<string> { "detail" };

        var merged = DevelopModules.SelectivePaste(target, source, sel);

        Assert.Contains(merged, o => o.OpType == DevelopBasicOp.Type && o.Params["v"] == "T");
        Assert.Contains(merged, o => o.OpType == SharpenOp.Type && o.Params["v"] == "S");
    }

    [Fact]
    public void SelectivePaste_SelectedModuleAbsentInSource_RemovesFromTarget()
    {
        // chọn module "basic" nhưng nguồn không có Basic -> đích bị gỡ Basic (reset module đó).
        var target = new List<EditOperation> { Op(DevelopBasicOp.Type, "T"), Op(SharpenOp.Type, "T") };
        var source = new List<EditOperation> { Op(SharpenOp.Type, "S") };
        var sel = new HashSet<string> { "basic" };

        var merged = DevelopModules.SelectivePaste(target, source, sel);

        Assert.DoesNotContain(merged, o => o.OpType == DevelopBasicOp.Type);
        // Sharpen (module detail không chọn) giữ của đích.
        Assert.Contains(merged, o => o.OpType == SharpenOp.Type && o.Params["v"] == "T");
    }

    [Fact]
    public void SelectivePaste_ResultIsCanonicallyOrdered()
    {
        var target = new List<EditOperation>();
        var source = new List<EditOperation> { Op(SharpenOp.Type), Op(CropOp.Type), Op(DevelopBasicOp.Type) };
        var sel = new HashSet<string> { "detail", "geometry", "basic" };

        var merged = DevelopModules.SelectivePaste(target, source, sel);

        Assert.Equal(CropOp.Type, merged[0].OpType);
        Assert.Equal(DevelopBasicOp.Type, merged[1].OpType);
        Assert.Equal(SharpenOp.Type, merged[2].OpType);
    }
}
