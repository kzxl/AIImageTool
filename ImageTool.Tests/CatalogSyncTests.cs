using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageTool.Core;
using ImageTool.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class CatalogSyncTests
{
    private static CatalogService NewDb(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "imgtool_sync_" + Guid.NewGuid().ToString("N") + ".db");
        return new CatalogService(dbPath);
    }

    private static string MakeImg(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        using var img = new Image<Rgba32>(8, 8, new Rgba32(100, 120, 140, 255));
        img.SaveAsJpeg(path);
        return path;
    }

    private static void Cleanup(string dbPath, string folder)
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(dbPath); } catch { }
        try { Directory.Delete(folder, true); } catch { }
    }

    [Fact]
    public async System.Threading.Tasks.Task SyncFolder_AddsNewFiles()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_syncf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            MakeImg(folder, "a.jpg");
            MakeImg(folder, "b.jpg");

            var r1 = await svc.SyncFolderAsync(folder, recursive: false);
            Assert.Equal(2, r1.Added);

            // thêm 1 file mới -> sync lại chỉ thêm 1.
            MakeImg(folder, "c.jpg");
            var r2 = await svc.SyncFolderAsync(folder, recursive: false);
            Assert.Equal(1, r2.Added);
            Assert.Equal(0, r2.Missing);

            // sync lần nữa không thêm gì.
            var r3 = await svc.SyncFolderAsync(folder, recursive: false);
            Assert.Equal(0, r3.Added);
        }
        finally { Cleanup(db, folder); }
    }

    [Fact]
    public async System.Threading.Tasks.Task SyncFolder_DetectsMissing()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_syncm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var a = MakeImg(folder, "a.jpg");
            MakeImg(folder, "b.jpg");
            await svc.SyncFolderAsync(folder, recursive: false);

            // xoá 1 file trên đĩa -> sync báo missing.
            File.Delete(a);
            var r = await svc.SyncFolderAsync(folder, recursive: false);
            Assert.Equal(1, r.Missing);
            Assert.Equal(0, r.Removed); // chưa gỡ (removeMissing=false)
            Assert.Contains(a, r.MissingPaths);

            // removeMissing=true -> gỡ khỏi catalog.
            var r2 = await svc.SyncFolderAsync(folder, recursive: false, removeMissing: true);
            Assert.Equal(1, r2.Removed);
        }
        finally { Cleanup(db, folder); }
    }

    [Fact]
    public async System.Threading.Tasks.Task SyncFolder_Recursive_IncludesSubfolders()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_syncr_" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(folder, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            MakeImg(folder, "top.jpg");
            MakeImg(sub, "deep.jpg");

            var rNonRec = await svc.SyncFolderAsync(folder, recursive: false);
            Assert.Equal(1, rNonRec.Added); // chỉ top

            var rRec = await svc.SyncFolderAsync(folder, recursive: true);
            Assert.Equal(1, rRec.Added); // thêm deep.jpg trong sub
        }
        finally { Cleanup(db, folder); }
    }

    [Fact]
    public async System.Threading.Tasks.Task Import_AddInPlace_KeepsOriginalPath()
    {
        var svc = NewDb(out var db);
        var folder = Path.Combine(Path.GetTempPath(), "imgtool_imp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var a = MakeImg(folder, "x.jpg");
            int n = await svc.ImportAsync(new[] { a }, new ImportOptions { Mode = ImportMode.AddInPlace });
            Assert.Equal(1, n);
            var img = svc.GetImage(a);
            Assert.NotNull(img);
            Assert.Equal(folder, img!.FolderPath); // giữ vị trí gốc, không copy
            Assert.Equal(8, img.Width);            // metadata đọc được qua Identify
        }
        finally { Cleanup(db, folder); }
    }
}
