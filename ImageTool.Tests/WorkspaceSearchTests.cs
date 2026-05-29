using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageTool.Core;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class WorkspaceSearchTests
{
    // Stub meta service: trả tags theo từng path đã set.
    private sealed class StubMeta : IImageMetaService
    {
        private readonly Dictionary<string, ImageMeta> _map = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string path, params string[] tags) => _map[path] = new ImageMeta { Tags = tags.ToList() };
        public ImageMeta Get(string imagePath) => _map.TryGetValue(imagePath, out var m) ? m : new ImageMeta();
        public void SetRating(string imagePath, int rating) { }
        public void SetLabel(string imagePath, ColorLabel label) { }
        public void SetPick(string imagePath, PickFlag pick) { }
        public void SetTags(string imagePath, IEnumerable<string> tags) { }
        public void SetDescription(string imagePath, string? description) { }
        public event EventHandler<ImageMetaChangedEventArgs>? MetaChanged;
    }

    [Fact]
    public void Search_MatchesKeyword_NotJustFilename()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_ws_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prevCtx = System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var a = Path.Combine(dir, "aaa.jpg"); File.WriteAllText(a, "x");
            var b = Path.Combine(dir, "bbb.jpg"); File.WriteAllText(b, "x");
            var c = Path.Combine(dir, "ccc.jpg"); File.WriteAllText(c, "x");

            var meta = new StubMeta();
            meta.Set(a, "Animal/Dog");
            meta.Set(b, "Place/Beach");
            // c: không tag.

            var ws = new WorkspaceService(meta);
            ws.OpenCatalogView(new[] { a, b, c }, "Test");

            // tìm "dog" -> chỉ khớp ảnh a (theo keyword, dù tên file không chứa "dog").
            ws.Filter.Search = "dog";
            ws.ApplyFilterAndSort();
            Assert.Single(ws.Images);
            Assert.Equal(a, ws.Images[0]);

            // tìm "Animal" (nhánh cha) -> vẫn khớp a.
            ws.Filter.Search = "Animal";
            ws.ApplyFilterAndSort();
            Assert.Single(ws.Images);

            // tìm theo tên file vẫn hoạt động.
            ws.Filter.Search = "ccc";
            ws.ApplyFilterAndSort();
            Assert.Single(ws.Images);
            Assert.Equal(c, ws.Images[0]);

            // không khớp -> rỗng.
            ws.Filter.Search = "zzz";
            ws.ApplyFilterAndSort();
            Assert.Empty(ws.Images);
        }
        finally { System.Threading.SynchronizationContext.SetSynchronizationContext(prevCtx); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Search_Null_ReturnsAll()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_ws_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prevCtx = System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var a = Path.Combine(dir, "one.jpg"); File.WriteAllText(a, "x");
            var b = Path.Combine(dir, "two.jpg"); File.WriteAllText(b, "x");
            var ws = new WorkspaceService(new StubMeta());
            ws.OpenCatalogView(new[] { a, b }, "Test");
            ws.Filter.Search = null;
            ws.ApplyFilterAndSort();
            Assert.Equal(2, ws.Images.Count);
        }
        finally { System.Threading.SynchronizationContext.SetSynchronizationContext(prevCtx); Directory.Delete(dir, true); }
    }
}
