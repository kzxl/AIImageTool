using ImageTool.Core;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class HistoryServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _img;

    public HistoryServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "imgtool_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_dir);
        _img = Path.Combine(_dir, "photo.jpg");
        File.WriteAllText(_img, "dummy"); // chỉ cần path tồn tại cho history sidecar
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static EditOperation Op(string plugin, string type, string val = "1")
        => new() { PluginId = plugin, OpType = type, Params = new() { ["v"] = val } };

    [Fact]
    public void Upsert_SameOpType_ReplacesInPlace()
    {
        var h = new HistoryService();
        h.Upsert(_img, Op("Develop", "DevelopBasic", "1"));
        h.Upsert(_img, Op("Develop", "DevelopBasic", "2"));

        var stack = h.GetStack(_img);
        Assert.Single(stack);                       // không tạo bước mới
        Assert.Equal("2", stack[0].Params["v"]);    // giá trị mới nhất
        Assert.Equal(1, h.GetPointer(_img));
    }

    [Fact]
    public void Upsert_DifferentOpType_Appends()
    {
        var h = new HistoryService();
        h.Upsert(_img, Op("Develop", "DevelopBasic"));
        h.Upsert(_img, Op("ColorLab", "ApplyLUT"));

        Assert.Equal(2, h.GetStack(_img).Count);
    }

    [Fact]
    public void UpsertGroup_RemovesDuplicatesAndKeepsOrder()
    {
        var h = new HistoryService();
        // Lần 1: chỉ Basic
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "1") });
        Assert.Single(h.GetStack(_img));

        // Lần 2: Basic + HSL -> không nhân đôi Basic, đúng 2 op.
        h.UpsertGroup(_img, "Develop", new[]
        {
            Op("Develop", "DevelopBasic", "2"),
            Op("Develop", "HslMixer", "5"),
        });
        var stack = h.GetStack(_img);
        Assert.Equal(2, stack.Count);
        Assert.Equal("DevelopBasic", stack[0].OpType);
        Assert.Equal("2", stack[0].Params["v"]);
        Assert.Equal("HslMixer", stack[1].OpType);
    }

    [Fact]
    public void UpsertGroup_PreservesOtherPluginOps()
    {
        var h = new HistoryService();
        h.Push(_img, Op("ColorLab", "ApplyLUT"));
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic") });
        // Re-apply Develop group -> không đụng op ColorLab.
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "9") });

        var stack = h.GetStack(_img);
        Assert.Equal(2, stack.Count);
        Assert.Contains(stack, o => o.PluginId == "ColorLab");
        Assert.Contains(stack, o => o.PluginId == "Develop" && o.Params["v"] == "9");
    }

    [Fact]
    public void UndoRedo_MovesPointer()
    {
        var h = new HistoryService();
        h.Push(_img, Op("Develop", "DevelopBasic"));
        h.Push(_img, Op("Develop", "HslMixer"));
        Assert.Equal(2, h.GetPointer(_img));

        h.Undo(_img);
        Assert.Equal(1, h.GetPointer(_img));
        h.Redo(_img);
        Assert.Equal(2, h.GetPointer(_img));
    }
}
