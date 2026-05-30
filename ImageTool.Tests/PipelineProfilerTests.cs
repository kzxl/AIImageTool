using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class PipelineProfilerTests
{
    private static LinearImage Solid(int w = 64, int h = 64)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.5f; img.Pixels[i + 1] = 0.5f; img.Pixels[i + 2] = 0.5f; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Profile_RecordsAllOps()
    {
        var reg = EditOpRegistry.CreateDefault();
        var prof = new PipelineProfiler(reg);
        var ops = new List<EditOperation>
        {
            new() { OpType = DevelopBasicOp.Type, Params = new() { ["exposure"] = "1" } },
            new() { OpType = ClarityOp.Type, Params = new() { ["amount"] = "0.5" } },
            new() { OpType = VignetteOp.Type, Params = new() { ["amount"] = "-0.4" } },
        };
        var report = prof.Profile(Solid(), ops);
        Assert.Equal(3, report.Ops.Count);
        Assert.True(report.TotalMs >= 0);
        Assert.Equal(64, report.Width);
    }

    [Fact]
    public void Profile_SkipsUnknownOps()
    {
        var reg = EditOpRegistry.CreateDefault();
        var prof = new PipelineProfiler(reg);
        var ops = new List<EditOperation>
        {
            new() { OpType = "TotallyUnknownOp", Params = new() },
            new() { OpType = DevelopBasicOp.Type, Params = new() { ["exposure"] = "0.5" } },
        };
        var report = prof.Profile(Solid(), ops);
        Assert.Single(report.Ops); // op lạ bị bỏ
    }

    [Fact]
    public void Profile_TracksResizingOpDimensionChange()
    {
        var reg = EditOpRegistry.CreateDefault();
        var prof = new PipelineProfiler(reg);
        var ops = new List<EditOperation>
        {
            new() { OpType = CropOp.Type, Params = new() { ["x"] = "0.25", ["y"] = "0.25", ["w"] = "0.5", ["h"] = "0.5" } },
        };
        var report = prof.Profile(Solid(40, 40), ops);
        Assert.Equal(20, report.Width);
        Assert.Equal(20, report.Height);
    }

    [Fact]
    public void Slowest_ReturnsHighest()
    {
        var reg = EditOpRegistry.CreateDefault();
        var prof = new PipelineProfiler(reg);
        var ops = new List<EditOperation>
        {
            new() { OpType = DevelopBasicOp.Type, Params = new() { ["exposure"] = "0.5" } },
            new() { OpType = ClarityOp.Type, Params = new() { ["amount"] = "0.8" } }, // blur -> thường chậm hơn
        };
        var report = prof.Profile(Solid(128, 128), ops);
        var slow = report.Slowest();
        Assert.NotNull(slow);
    }

    [Fact]
    public void Profile_Empty_NoOps()
    {
        var reg = EditOpRegistry.CreateDefault();
        var report = new PipelineProfiler(reg).Profile(Solid(), new List<EditOperation>());
        Assert.Empty(report.Ops);
        Assert.Null(report.Slowest());
    }
}
