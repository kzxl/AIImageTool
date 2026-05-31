using System.Collections.Generic;
using System.Threading;
using ImageTool.Core;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class AutoSaveServiceTests
{
    // Stub history chi can event HistoryChanged.
    private sealed class StubHistory : IHistoryService
    {
        public event System.EventHandler<HistoryChangedEventArgs>? HistoryChanged;
        public void Raise(string path, int ptr)
            => HistoryChanged?.Invoke(this, new HistoryChangedEventArgs(path, new List<EditOperation>(), ptr));

        // Phần còn lại của interface: no-op cho test.
        public IReadOnlyList<EditOperation> GetStack(string imagePath) => new List<EditOperation>();
        public int GetPointer(string imagePath) => 0;
        public void Push(string imagePath, EditOperation op) { }
        public void Upsert(string imagePath, EditOperation op) { }
        public void UpsertGroup(string imagePath, string pluginId, IReadOnlyList<EditOperation> ops) { }
        public EditOperation? Undo(string imagePath) => null;
        public EditOperation? Redo(string imagePath) => null;
        public void SetPointer(string imagePath, int pointer) { }
        public void Clear(string imagePath) { }
        public void SaveSnapshot(string imagePath, string name) { }
        public bool ApplySnapshot(string imagePath, string name) => false;
        public bool DeleteSnapshot(string imagePath, string name) => false;
        public IReadOnlyList<HistorySnapshot> GetSnapshots(string imagePath) => new List<HistorySnapshot>();
    }

    [Fact]
    public void Debounce_WritesOnceAfterRapidChanges()
    {
        var history = new StubHistory();
        int writes = 0; string? lastPath = null;
        using var svc = new AutoSaveService(history, (p, ops, ptr) => { Interlocked.Increment(ref writes); lastPath = p; }, 150);

        // 5 thay đổi liên tiếp trong < debounce.
        for (int i = 0; i < 5; i++) { history.Raise("a.jpg", i); Thread.Sleep(10); }

        Thread.Sleep(500);        // qua debounce (margin rộng chống flake)
        Assert.Equal(1, writes);  // gộp 5 thay đổi -> chỉ ghi 1 lần
        Assert.Equal("a.jpg", lastPath);
    }

    [Fact]
    public void SeparateImages_EachGetWrite()
    {
        var history = new StubHistory();
        var written = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var svc = new AutoSaveService(history, (p, ops, ptr) => written.Add(p), 120);

        history.Raise("a.jpg", 1);
        history.Raise("b.jpg", 1);
        Thread.Sleep(600);

        Assert.Contains("a.jpg", written);
        Assert.Contains("b.jpg", written);
    }

    [Fact]
    public void FlushAll_WritesPendingImmediately()
    {
        var history = new StubHistory();
        int writes = 0;
        using var svc = new AutoSaveService(history, (p, ops, ptr) => Interlocked.Increment(ref writes), 5000);

        history.Raise("a.jpg", 1);
        Assert.Equal(0, writes);   // debounce dài, chưa ghi
        svc.FlushAll();
        Assert.Equal(1, writes);   // flush ngay
    }

    [Fact]
    public void Dispose_FlushesPending_AndStopsListening()
    {
        var history = new StubHistory();
        int writes = 0;
        var svc = new AutoSaveService(history, (p, ops, ptr) => Interlocked.Increment(ref writes), 5000);
        history.Raise("a.jpg", 1);
        svc.Dispose();
        Assert.Equal(1, writes); // dispose flush

        // Sau dispose, event không còn ghi.
        history.Raise("b.jpg", 1);
        Thread.Sleep(50);
        Assert.Equal(1, writes);
    }
}
