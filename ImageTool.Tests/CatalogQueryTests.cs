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

    [Fact]
    public void CurationFilters_AddClauses()
    {
        var (sql, _) = CatalogService.BuildAdvancedSql(new CatalogQuery
        {
            RatingMin = 3,
            Label = ColorLabel.Red,
            Pick = PickFlag.Pick,
            Keyword = "sunset"
        });
        Assert.Contains("Rating >= @ratingMin", sql);
        Assert.Contains("Label = @label", sql);
        Assert.Contains("Pick = @pick", sql);
        Assert.Contains("Keywords LIKE @kw", sql);
    }

    [Fact]
    public void RatingSort_IsSupported()
    {
        var (sql, _) = CatalogService.BuildAdvancedSql(new CatalogQuery { SortField = CatalogSortField.Rating });
        Assert.Contains("ORDER BY Rating DESC", sql);
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

public class SmartCollectionTests
{
    private static CatalogService NewDb(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "imgtool_sc_" + Guid.NewGuid().ToString("N") + ".db");
        return new CatalogService(path);
    }

    private static void Seed(string dbPath, string fileName, string? make = null, int? iso = null)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO CatalogImage (FilePath, FileName, FolderPath, FileSize, ImportedAt, ImportMode, CameraMake, Iso)
                            VALUES ($fp, $fn, $folder, 1000, $imp, 0, $make, $iso)";
        cmd.Parameters.AddWithValue("$fp", Path.Combine("C:\\t", fileName));
        cmd.Parameters.AddWithValue("$fn", fileName);
        cmd.Parameters.AddWithValue("$folder", "C:\\t");
        cmd.Parameters.AddWithValue("$imp", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$make", (object?)make ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iso", (object?)iso ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void TryDelete(string path)
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public void Create_And_Get_RoundTripsQuery()
    {
        var svc = NewDb(out var path);
        try
        {
            var q = new CatalogQuery { CameraMake = "Canon", IsoMax = 800 };
            var sc = svc.CreateSmartCollection("Low ISO Canon", q);
            Assert.True(sc.Id > 0);

            var all = svc.GetSmartCollections();
            Assert.Single(all);
            Assert.Equal("Low ISO Canon", all[0].Name);
            Assert.Equal("Canon", all[0].Query.CameraMake);
            Assert.Equal(800, all[0].Query.IsoMax);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void GetImages_ResolvesDynamically()
    {
        var svc = NewDb(out var path);
        try
        {
            Seed(path, "a.jpg", make: "Canon", iso: 100);
            Seed(path, "b.jpg", make: "Canon", iso: 1600);
            Seed(path, "c.jpg", make: "Nikon", iso: 100);
            var sc = svc.CreateSmartCollection("Canon low", new CatalogQuery { CameraMake = "Canon", IsoMax = 800 });

            var imgs = svc.GetSmartCollectionImages(sc.Id);
            Assert.Single(imgs);
            Assert.Equal("a.jpg", imgs[0].FileName);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void ImageCount_ReflectsCurrentMatches()
    {
        var svc = NewDb(out var path);
        try
        {
            Seed(path, "a.jpg", make: "Canon");
            Seed(path, "b.jpg", make: "Canon");
            var sc = svc.CreateSmartCollection("All Canon", new CatalogQuery { CameraMake = "Canon" });
            Assert.Equal(2, svc.GetSmartCollections()[0].ImageCount);

            Seed(path, "c.jpg", make: "Canon");
            Assert.Equal(3, svc.GetSmartCollections()[0].ImageCount);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Update_And_Delete_Work()
    {
        var svc = NewDb(out var path);
        try
        {
            var sc = svc.CreateSmartCollection("X", new CatalogQuery { IsoMin = 100 });
            svc.UpdateSmartCollection(sc.Id, "Y", new CatalogQuery { IsoMin = 200 });
            var updated = svc.GetSmartCollections()[0];
            Assert.Equal("Y", updated.Name);
            Assert.Equal(200, updated.Query.IsoMin);

            svc.DeleteSmartCollection(sc.Id);
            Assert.Empty(svc.GetSmartCollections());
        }
        finally { TryDelete(path); }
    }
}
