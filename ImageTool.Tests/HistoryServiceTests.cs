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

    [Fact]
    public void Snapshot_SaveAndApply_RestoresState()
    {
        var h = new HistoryService();
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "1") });
        h.SaveSnapshot(_img, "v1");

        // Chỉnh tiếp khác đi.
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "9"), Op("Develop", "HslMixer", "5") });
        Assert.Equal(2, h.GetStack(_img).Count);

        // Áp lại snapshot v1 -> về đúng 1 op giá trị "1".
        Assert.True(h.ApplySnapshot(_img, "v1"));
        var stack = h.GetStack(_img);
        Assert.Single(stack);
        Assert.Equal("1", stack[0].Params["v"]);
    }

    [Fact]
    public void Snapshot_IsImmutable_AfterFurtherEdits()
    {
        var h = new HistoryService();
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "3") });
        h.SaveSnapshot(_img, "keep");
        // Chỉnh tiếp không được làm thay đổi nội dung snapshot.
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "7") });

        var snaps = h.GetSnapshots(_img);
        Assert.Single(snaps);
        Assert.Equal("keep", snaps[0].Name);
        Assert.Single(snaps[0].Ops);
        Assert.Equal("3", snaps[0].Ops[0].Params["v"]);
    }

    [Fact]
    public void Snapshot_OverwritesSameName()
    {
        var h = new HistoryService();
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "1") });
        h.SaveSnapshot(_img, "x");
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "2") });
        h.SaveSnapshot(_img, "x"); // cùng tên -> ghi đè

        var snaps = h.GetSnapshots(_img);
        Assert.Single(snaps);
        Assert.Equal("2", snaps[0].Ops[0].Params["v"]);
    }

    [Fact]
    public void Snapshot_Delete_Works()
    {
        var h = new HistoryService();
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "1") });
        h.SaveSnapshot(_img, "a");
        h.SaveSnapshot(_img, "b");
        Assert.Equal(2, h.GetSnapshots(_img).Count);

        Assert.True(h.DeleteSnapshot(_img, "a"));
        Assert.False(h.DeleteSnapshot(_img, "missing"));
        var snaps = h.GetSnapshots(_img);
        Assert.Single(snaps);
        Assert.Equal("b", snaps[0].Name);
    }

    [Fact]
    public void Snapshot_PersistsAcrossInstances()
    {
        var h1 = new HistoryService();
        h1.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "4") });
        h1.SaveSnapshot(_img, "persisted");

        // Instance mới đọc lại từ sidecar đĩa.
        var h2 = new HistoryService();
        var snaps = h2.GetSnapshots(_img);
        Assert.Single(snaps);
        Assert.Equal("persisted", snaps[0].Name);
        Assert.True(h2.ApplySnapshot(_img, "persisted"));
        Assert.Equal("4", h2.GetStack(_img)[0].Params["v"]);
    }

    [Fact]
    public void Snapshot_ApplyMissing_ReturnsFalse()
    {
        var h = new HistoryService();
        Assert.False(h.ApplySnapshot(_img, "nope"));
    }

    [Fact]
    public void Snapshot_EmptyName_Ignored()
    {
        var h = new HistoryService();
        h.UpsertGroup(_img, "Develop", new[] { Op("Develop", "DevelopBasic", "1") });
        h.SaveSnapshot(_img, "   ");
        Assert.Empty(h.GetSnapshots(_img));
    }
}
