using System;
using System.IO;
using System.Linq;
using ImageTool.Core;
using ImageTool.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

// Xác nhận CatalogService quản lý Collection đầy đủ (8.1): tạo/đổi tên/xoá + thêm/gỡ ảnh.
public class CatalogCollectionTests
{
    private static CatalogService NewDb(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "imgtool_coll_" + Guid.NewGuid().ToString("N") + ".db");
        return new CatalogService(dbPath);
    }

    private static string MakeImg(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        using var img = new Image<Rgba32>(8, 8, new Rgba32(80, 90, 100, 255));
        img.SaveAsJpeg(path);
        return path;
    }

    private static void Cleanup(string dbPath, string folder)
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(dbPath); } catch { }
        try { Directory.Delete(folder, true); } catch { }
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateRenameDelete_Collection()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_collf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var c = svc.CreateCollection("Trip", "summer");
            Assert.True(c.Id > 0);
            Assert.Contains(svc.GetCollections(), x => x.Id == c.Id && x.Name == "Trip");

            svc.RenameCollection(c.Id, "Holiday");
            Assert.Contains(svc.GetCollections(), x => x.Id == c.Id && x.Name == "Holiday");

            svc.DeleteCollection(c.Id);
            Assert.DoesNotContain(svc.GetCollections(), x => x.Id == c.Id);
            await System.Threading.Tasks.Task.CompletedTask;
        }
        finally { Cleanup(db, folder); }
    }

    [Fact]
    public async System.Threading.Tasks.Task AddRemove_ImagesInCollection()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_collf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var a = MakeImg(folder, "a.jpg");
            var b = MakeImg(folder, "b.jpg");
            await svc.ImportAsync(new[] { a, b }, new ImportOptions { Mode = ImportMode.AddInPlace });

            var c = svc.CreateCollection("Set");
            svc.AddToCollection(c.Id, new[] { a, b });
            var imgs = svc.GetCollectionImages(c.Id);
            Assert.Equal(2, imgs.Count);

            svc.RemoveFromCollection(c.Id, new[] { a });
            imgs = svc.GetCollectionImages(c.Id);
            Assert.Single(imgs);
            Assert.Equal("b.jpg", imgs[0].FileName);
        }
        finally { Cleanup(db, folder); }
    }

    [Fact]
    public async System.Threading.Tasks.Task AddToCollection_IgnoresUnimportedAndDuplicates()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_collf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var a = MakeImg(folder, "a.jpg");
            var ghost = Path.Combine(folder, "ghost.jpg"); // chưa import
            await svc.ImportAsync(new[] { a }, new ImportOptions { Mode = ImportMode.AddInPlace });

            var c = svc.CreateCollection("Set");
            svc.AddToCollection(c.Id, new[] { a, ghost });   // ghost bị bỏ qua
            svc.AddToCollection(c.Id, new[] { a });          // trùng -> INSERT OR IGNORE
            Assert.Single(svc.GetCollectionImages(c.Id));
        }
        finally { Cleanup(db, folder); }
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteCollection_RemovesMembership_NotImages()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_collf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var a = MakeImg(folder, "a.jpg");
            await svc.ImportAsync(new[] { a }, new ImportOptions { Mode = ImportMode.AddInPlace });
            var c = svc.CreateCollection("Set");
            svc.AddToCollection(c.Id, new[] { a });
            svc.DeleteCollection(c.Id);
            // Ảnh vẫn còn trong catalog, chỉ membership bị xoá.
            Assert.NotNull(svc.GetImage(a));
        }
        finally { Cleanup(db, folder); }
    }
}
