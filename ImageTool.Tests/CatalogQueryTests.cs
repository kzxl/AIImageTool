using System;
using System.IO;
using System.Linq;
using ImageTool.Core;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class CatalogQueryTests
{
    // ---- SQL builder (logic thuần) ----

    [Fact]
    public void EmptyQuery_NoWhereClause()
    {
        var (sql, _) = CatalogService.BuildAdvancedSql(new CatalogQuery());
        Assert.DoesNotContain("WHERE", sql);
        Assert.Contains("ORDER BY ImportedAt DESC", sql);
    }

    [Fact]
    public void IsoRange_AddsBothBounds()
    {
        var (sql, _) = CatalogService.BuildAdvancedSql(new CatalogQuery { IsoMin = 100, IsoMax = 800 });
        Assert.Contains("Iso >= @isoMin", sql);
        Assert.Contains("Iso <= @isoMax", sql);
        Assert.Contains(" AND ", sql);
    }

    [Fact]
    public void Sort_RespectsFieldAndDirection()
    {
        var (sql, _) = CatalogService.BuildAdvancedSql(new CatalogQuery { SortField = CatalogSortField.FileName, SortDescending = false });
        Assert.Contains("ORDER BY FileName ASC", sql);
    }

    [Fact]
    public void TextFilter_UsesParameterNotInline()
    {
        // chống injection: text phải đi vào tham số, không nhúng vào SQL.
        var (sql, _) = CatalogService.BuildAdvancedSql(new CatalogQuery { Text = "'; DROP TABLE x; --" });
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.Contains("@text", sql);
    }

    // ---- Integration (DB tạm) ----

    private static CatalogService NewDb(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "imgtool_test_" + Guid.NewGuid().ToString("N") + ".db");
        return new CatalogService(path);
    }

    [Fact]
    public void SearchAdvanced_FiltersByIso_Integration()
    {
        var svc = NewDb(out var path);
        try
        {
            // chèn trực tiếp 3 ảnh (không cần file thật) qua reflection-free đường: dùng import giả lập không khả thi,
            // nên seed bằng raw connection.
            SeedImage(path, "a.jpg", iso: 100);
            SeedImage(path, "b.jpg", iso: 400);
            SeedImage(path, "c.jpg", iso: 1600);

            var res = svc.SearchAdvanced(new CatalogQuery { IsoMin = 200, IsoMax = 800 });
            Assert.Single(res);
            Assert.Equal("b.jpg", res[0].FileName);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void SearchAdvanced_FiltersByCameraAndSorts_Integration()
    {
        var svc = NewDb(out var path);
        try
        {
            SeedImage(path, "z.jpg", make: "Canon", iso: 100);
            SeedImage(path, "a.jpg", make: "Canon", iso: 200);
            SeedImage(path, "n.jpg", make: "Nikon", iso: 100);

            var res = svc.SearchAdvanced(new CatalogQuery { CameraMake = "Canon", SortField = CatalogSortField.FileName, SortDescending = false });
            Assert.Equal(2, res.Count);
            Assert.Equal("a.jpg", res[0].FileName); // sort theo tên tăng dần
            Assert.Equal("z.jpg", res[1].FileName);
        }
        finally { TryDelete(path); }
    }

    private static void SeedImage(string dbPath, string fileName, string? make = null, int? iso = null)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO CatalogImage (FilePath, FileName, FolderPath, FileSize, ImportedAt, ImportMode, CameraMake, Iso)
                            VALUES ($fp, $fn, $folder, 1000, $imp, 0, $make, $iso)";
        cmd.Parameters.AddWithValue("$fp", Path.Combine("C:\\test", fileName));
        cmd.Parameters.AddWithValue("$fn", fileName);
        cmd.Parameters.AddWithValue("$folder", "C:\\test");
        cmd.Parameters.AddWithValue("$imp", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$make", (object?)make ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iso", (object?)iso ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void TryDelete(string path)
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
