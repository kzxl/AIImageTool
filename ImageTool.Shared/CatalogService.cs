using System.Data;
using Microsoft.Data.Sqlite;
using Dapper;
using ImageTool.Core;

namespace ImageTool.Shared;

public class CatalogService : ICatalogService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public event EventHandler<ImportCompletedEventArgs>? ImportCompleted;
    public event EventHandler? CollectionsChanged;

    public CatalogService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImageTool");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "catalog.db");
        _connectionString = $"Data Source={_dbPath}";
        EnsureSchema();
    }

    /// <summary>Ctor cho test: chỉ định đường dẫn DB (vd file tạm). Không đụng AppData.</summary>
    public CatalogService(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = $"Data Source={_dbPath}";
        EnsureSchema();
    }

    private IDbConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        conn.Execute("PRAGMA journal_mode=WAL;");
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        conn.Execute("""
            CREATE TABLE IF NOT EXISTS CatalogImage (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath    TEXT NOT NULL UNIQUE,
                FileName    TEXT NOT NULL,
                FolderPath  TEXT NOT NULL,
                FileSize    INTEGER,
                ImportedAt  TEXT NOT NULL,
                ImportMode  INTEGER NOT NULL DEFAULT 0,
                OriginalPath TEXT,
                DateTaken       TEXT,
                CameraMake      TEXT,
                CameraModel     TEXT,
                LensModel       TEXT,
                FocalLength     REAL,
                Aperture        REAL,
                ShutterSpeed    TEXT,
                Iso             INTEGER,
                Width           INTEGER,
                Height          INTEGER,
                Orientation     INTEGER
            );
            CREATE INDEX IF NOT EXISTS IX_CatalogImage_FolderPath ON CatalogImage(FolderPath);
            CREATE INDEX IF NOT EXISTS IX_CatalogImage_FileName ON CatalogImage(FileName);
            CREATE INDEX IF NOT EXISTS IX_CatalogImage_DateTaken ON CatalogImage(DateTaken);

            CREATE TABLE IF NOT EXISTS Collection (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT NOT NULL,
                Description TEXT,
                CreatedAt   TEXT NOT NULL,
                SortOrder   INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS CollectionImage (
                CollectionId INTEGER NOT NULL REFERENCES Collection(Id) ON DELETE CASCADE,
                ImageId      INTEGER NOT NULL REFERENCES CatalogImage(Id) ON DELETE CASCADE,
                SortOrder    INTEGER NOT NULL DEFAULT 0,
                AddedAt      TEXT NOT NULL,
                PRIMARY KEY (CollectionId, ImageId)
            );
            CREATE INDEX IF NOT EXISTS IX_CollectionImage_ImageId ON CollectionImage(ImageId);
            """);

        conn.Execute("""
            CREATE TABLE IF NOT EXISTS SmartCollection (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT NOT NULL,
                QueryJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """);

        // Migration: thêm cột GPS nếu DB cũ chưa có (ALTER an toàn, bọc try vì SQLite không có IF NOT EXISTS cho cột).
        AddColumnIfMissing(conn, "CatalogImage", "GpsLatitude", "REAL");
        AddColumnIfMissing(conn, "CatalogImage", "GpsLongitude", "REAL");
    }

    private static void AddColumnIfMissing(IDbConnection conn, string table, string column, string type)
    {
        try
        {
            var cols = conn.Query<dynamic>($"PRAGMA table_info({table})");
            bool exists = cols.Any(c => string.Equals((string)c.name, column, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                conn.Execute($"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }
        catch (Exception ex) { AppLog.Warn("Catalog.Migrate", $"{table}.{column}: {ex.Message}"); }
    }

    public async Task<int> ImportAsync(IEnumerable<string> filePaths, ImportOptions options,
        IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var files = filePaths.ToList();
        int total = files.Count;
        var importedPaths = new List<string>();

        await Task.Run(() =>
        {
            // 1) CopyToLibrary (nếu cần) chạy trước, ánh xạ target -> original.
            var targets = new List<(string Target, string? Original, string Source)>(files.Count);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (options.Mode == ImportMode.CopyToLibrary && !string.IsNullOrEmpty(options.DestinationFolder))
                {
                    var tgt = CopyToLibrary(file, options);
                    targets.Add((tgt, file, file));
                }
                else targets.Add((file, null, file));
            }

            // 2) Batch existence check: 1 truy vấn lấy toàn bộ FilePath đã có -> HashSet (thay vì N query).
            HashSet<string> existing;
            using (var conn0 = Open())
            {
                existing = new HashSet<string>(
                    conn0.Query<string>("SELECT FilePath FROM CatalogImage"),
                    StringComparer.OrdinalIgnoreCase);
            }

            var toInsert = targets.Where(t => !existing.Contains(t.Target)).ToList();

            // 3) Đọc metadata SONG SONG (Image.Identify header-only) — phần tốn thời gian nhất.
            int processed = 0;
            var metas = new CatalogImage[toInsert.Count];
            var parallelOpts = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)
            };
            Parallel.For(0, toInsert.Count, parallelOpts, i =>
            {
                var t = toInsert[i];
                var meta = ExifReader.ReadMetadata(t.Target);
                meta.ImportedAt = DateTime.UtcNow;
                meta.ImportMode = options.Mode;
                meta.OriginalPath = t.Original;
                metas[i] = meta;

                int done = Interlocked.Increment(ref processed);
                progress?.Report(new ImportProgress { Total = total, Completed = done, CurrentFile = meta.FileName });
            });

            // 4) Ghi DB 1 transaction (nhanh, tuần tự — SQLite không ghi song song).
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            foreach (var meta in metas)
            {
                if (meta == null) continue;
                conn.Execute("""
                    INSERT INTO CatalogImage (FilePath, FileName, FolderPath, FileSize, ImportedAt, ImportMode, OriginalPath,
                        DateTaken, CameraMake, CameraModel, LensModel, FocalLength, Aperture, ShutterSpeed, Iso, Width, Height, Orientation, GpsLatitude, GpsLongitude)
                    VALUES (@FilePath, @FileName, @FolderPath, @FileSize, @ImportedAt, @ImportMode, @OriginalPath,
                        @DateTaken, @CameraMake, @CameraModel, @LensModel, @FocalLength, @Aperture, @ShutterSpeed, @Iso, @Width, @Height, @Orientation, @GpsLatitude, @GpsLongitude)
                    """, meta, tx);
                importedPaths.Add(meta.FilePath);
            }
            tx.Commit();
        }, ct);

        ImportCompleted?.Invoke(this, new ImportCompletedEventArgs(importedPaths.Count, importedPaths));
        return importedPaths.Count;
    }

    private static readonly string[] _imageExts =
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff",
          ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef", ".srw", ".raw", ".nrw", ".sr2" };

    public async Task<SyncResult> SyncFolderAsync(string folderPath, bool recursive, bool removeMissing = false,
        IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new SyncResult();
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return result;

        // 1) Quét file ảnh trên đĩa.
        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        List<string> onDisk;
        try
        {
            onDisk = Directory.EnumerateFiles(folderPath, "*.*", opt)
                .Where(f => Array.IndexOf(_imageExts, Path.GetExtension(f).ToLowerInvariant()) >= 0)
                .ToList();
        }
        catch (Exception ex) { AppLog.Error("Catalog.Sync", folderPath, ex); return result; }

        // 2) Lấy entry catalog hiện có trong folder.
        var inCatalog = recursive
            ? GetImagesUnderFolder(folderPath)
            : GetImagesByFolder(folderPath);
        var catalogSet = new HashSet<string>(inCatalog.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
        var diskSet = new HashSet<string>(onDisk, StringComparer.OrdinalIgnoreCase);

        // 3) File mới trên đĩa chưa có trong catalog -> import in-place.
        var newFiles = onDisk.Where(f => !catalogSet.Contains(f)).ToList();
        if (newFiles.Count > 0)
            result.Added = await ImportAsync(newFiles, new ImportOptions { Mode = ImportMode.AddInPlace }, progress, ct);

        // 4) Entry catalog mà file không còn trên đĩa.
        var missing = inCatalog.Where(i => !diskSet.Contains(i.FilePath)).Select(i => i.FilePath).ToList();
        result.Missing = missing.Count;
        result.MissingPaths = missing;
        if (removeMissing && missing.Count > 0)
        {
            RemoveFromCatalog(missing);
            result.Removed = missing.Count;
        }

        return result;
    }

    /// <summary>Lấy mọi ảnh catalog nằm dưới folder (đệ quy, theo tiền tố FolderPath).</summary>
    private IReadOnlyList<CatalogImage> GetImagesUnderFolder(string folderPath)
    {
        using var conn = Open();
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return conn.Query<CatalogImage>(
            "SELECT * FROM CatalogImage WHERE FolderPath = @prefix OR FolderPath LIKE @likePat OR FolderPath LIKE @likePatAlt",
            new { prefix, likePat = prefix + Path.DirectorySeparatorChar + "%", likePatAlt = prefix + Path.AltDirectorySeparatorChar + "%" }).ToList();
    }

    private string CopyToLibrary(string sourcePath, ImportOptions options)
    {
        var destRoot = options.DestinationFolder!;
        string subFolder;

        if (options.SubfolderByDate)
        {
            var fi = new FileInfo(sourcePath);
            var date = fi.LastWriteTime;
            subFolder = Path.Combine(date.ToString("yyyy"), date.ToString("yyyy-MM-dd"));
        }
        else
        {
            subFolder = "";
        }

        var destDir = Path.Combine(destRoot, subFolder);
        Directory.CreateDirectory(destDir);

        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(destDir, fileName);

        if (File.Exists(destPath))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(destDir, $"{name}_{Guid.NewGuid():N[..6]}{ext}");
        }

        File.Copy(sourcePath, destPath, false);
        return destPath;
    }

    public bool IsImported(string filePath)
    {
        using var conn = Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM CatalogImage WHERE FilePath = @filePath", new { filePath }) > 0;
    }

    public bool IsFolderImported(string folderPath)
    {
        return CountImportedInFolder(folderPath) > 0;
    }

    public int CountImportedInFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return 0;
        using var conn = Open();
        // FolderPath khớp chính xác hoặc là tiền tố (có thêm separator).
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefixSep = prefix + Path.DirectorySeparatorChar;
        var prefixSepAlt = prefix + Path.AltDirectorySeparatorChar;
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM CatalogImage WHERE FolderPath = @prefix OR FolderPath LIKE @likePat OR FolderPath LIKE @likePatAlt",
            new { prefix, likePat = prefixSep + "%", likePatAlt = prefixSepAlt + "%" });
    }

    public IReadOnlyList<CatalogImage> GetAllImages()
    {
        using var conn = Open();
        return conn.Query<CatalogImage>("SELECT * FROM CatalogImage ORDER BY ImportedAt DESC").ToList();
    }

    public IReadOnlyList<CatalogImage> GetImagesByFolder(string folderPath)
    {
        using var conn = Open();
        return conn.Query<CatalogImage>("SELECT * FROM CatalogImage WHERE FolderPath = @folderPath ORDER BY FileName", new { folderPath }).ToList();
    }

    public IReadOnlyList<CatalogImage> Search(string query)
    {
        using var conn = Open();
        var pattern = $"%{query}%";
        return conn.Query<CatalogImage>(
            "SELECT * FROM CatalogImage WHERE FileName LIKE @pattern OR FolderPath LIKE @pattern ORDER BY ImportedAt DESC",
            new { pattern }).ToList();
    }

    /// <summary>
    /// Tìm kiếm nâng cao (8.4): build câu WHERE động theo các tiêu chí non-null của CatalogQuery,
    /// kết hợp AND, dùng tham số hoá (chống SQL injection). Sắp theo SortField/SortDescending.
    /// </summary>
    public IReadOnlyList<CatalogImage> SearchAdvanced(CatalogQuery query)
    {
        using var conn = Open();
        var (sql, parameters) = BuildAdvancedSql(query);
        return conn.Query<CatalogImage>(sql, parameters).ToList();
    }

    /// <summary>Dựng SQL + tham số cho CatalogQuery. Tách riêng (public) để unit test được câu lệnh.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildAdvancedSql(CatalogQuery q)
    {
        var where = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            where.Add("(FileName LIKE @text OR FolderPath LIKE @text)");
            p.Add("text", $"%{q.Text.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(q.CameraMake))
        {
            where.Add("CameraMake LIKE @make");
            p.Add("make", $"%{q.CameraMake.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(q.CameraModel))
        {
            where.Add("CameraModel LIKE @model");
            p.Add("model", $"%{q.CameraModel.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(q.LensModel))
        {
            where.Add("LensModel LIKE @lens");
            p.Add("lens", $"%{q.LensModel.Trim()}%");
        }
        if (q.IsoMin.HasValue) { where.Add("Iso >= @isoMin"); p.Add("isoMin", q.IsoMin.Value); }
        if (q.IsoMax.HasValue) { where.Add("Iso <= @isoMax"); p.Add("isoMax", q.IsoMax.Value); }
        if (q.ApertureMin.HasValue) { where.Add("Aperture >= @apMin"); p.Add("apMin", q.ApertureMin.Value); }
        if (q.ApertureMax.HasValue) { where.Add("Aperture <= @apMax"); p.Add("apMax", q.ApertureMax.Value); }
        if (q.FocalMin.HasValue) { where.Add("FocalLength >= @fMin"); p.Add("fMin", q.FocalMin.Value); }
        if (q.FocalMax.HasValue) { where.Add("FocalLength <= @fMax"); p.Add("fMax", q.FocalMax.Value); }
        if (q.DateFrom.HasValue) { where.Add("DateTaken >= @dFrom"); p.Add("dFrom", q.DateFrom.Value.ToString("o")); }
        if (q.DateTo.HasValue) { where.Add("DateTaken <= @dTo"); p.Add("dTo", q.DateTo.Value.ToString("o")); }

        string whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        string sortCol = q.SortField switch
        {
            CatalogSortField.FileName => "FileName",
            CatalogSortField.DateTaken => "DateTaken",
            CatalogSortField.Iso => "Iso",
            CatalogSortField.FileSize => "FileSize",
            CatalogSortField.Aperture => "Aperture",
            CatalogSortField.FocalLength => "FocalLength",
            _ => "ImportedAt"
        };
        string dir = q.SortDescending ? "DESC" : "ASC";
        string sql = $"SELECT * FROM CatalogImage {whereClause} ORDER BY {sortCol} {dir}";
        return (sql, p);
    }

    public CatalogImage? GetImage(string filePath)
    {
        using var conn = Open();
        return conn.QueryFirstOrDefault<CatalogImage>("SELECT * FROM CatalogImage WHERE FilePath = @filePath", new { filePath });
    }

    public void RemoveFromCatalog(IEnumerable<string> filePaths)
    {
        using var conn = Open();
        foreach (var path in filePaths)
            conn.Execute("DELETE FROM CatalogImage WHERE FilePath = @path", new { path });
    }

    public IReadOnlyList<ImageCollection> GetCollections()
    {
        using var conn = Open();
        return conn.Query<ImageCollection>("""
            SELECT c.*, (SELECT COUNT(*) FROM CollectionImage ci WHERE ci.CollectionId = c.Id) AS ImageCount
            FROM Collection c ORDER BY c.SortOrder, c.Name
            """).ToList();
    }

    public ImageCollection CreateCollection(string name, string? description = null)
    {
        using var conn = Open();
        var now = DateTime.UtcNow;
        var id = conn.ExecuteScalar<long>(
            "INSERT INTO Collection (Name, Description, CreatedAt, SortOrder) VALUES (@name, @description, @now, 0); SELECT last_insert_rowid();",
            new { name, description, now });
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
        return new ImageCollection { Id = id, Name = name, Description = description, CreatedAt = now };
    }

    public void RenameCollection(long collectionId, string newName)
    {
        using var conn = Open();
        conn.Execute("UPDATE Collection SET Name = @newName WHERE Id = @collectionId", new { newName, collectionId });
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteCollection(long collectionId)
    {
        using var conn = Open();
        conn.Execute("DELETE FROM Collection WHERE Id = @collectionId", new { collectionId });
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddToCollection(long collectionId, IEnumerable<string> filePaths)
    {
        using var conn = Open();
        foreach (var path in filePaths)
        {
            var imageId = conn.ExecuteScalar<long?>("SELECT Id FROM CatalogImage WHERE FilePath = @path", new { path });
            if (imageId == null) continue;
            conn.Execute("""
                INSERT OR IGNORE INTO CollectionImage (CollectionId, ImageId, SortOrder, AddedAt)
                VALUES (@collectionId, @imageId, 0, @now)
                """, new { collectionId, imageId, now = DateTime.UtcNow });
        }
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFromCollection(long collectionId, IEnumerable<string> filePaths)
    {
        using var conn = Open();
        foreach (var path in filePaths)
        {
            var imageId = conn.ExecuteScalar<long?>("SELECT Id FROM CatalogImage WHERE FilePath = @path", new { path });
            if (imageId == null) continue;
            conn.Execute("DELETE FROM CollectionImage WHERE CollectionId = @collectionId AND ImageId = @imageId",
                new { collectionId, imageId });
        }
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<CatalogImage> GetCollectionImages(long collectionId)
    {
        using var conn = Open();
        return conn.Query<CatalogImage>("""
            SELECT ci.* FROM CatalogImage ci
            INNER JOIN CollectionImage col ON col.ImageId = ci.Id
            WHERE col.CollectionId = @collectionId
            ORDER BY col.SortOrder, ci.FileName
            """, new { collectionId }).ToList();
    }

    // ===== Smart Collections (8.3) =====
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { IncludeFields = false };

    public IReadOnlyList<SmartCollection> GetSmartCollections()
    {
        using var conn = Open();
        var rows = conn.Query<(long Id, string Name, string QueryJson, string CreatedAt)>(
            "SELECT Id, Name, QueryJson, CreatedAt FROM SmartCollection ORDER BY Name").ToList();
        var result = new List<SmartCollection>(rows.Count);
        foreach (var r in rows)
        {
            var query = DeserializeQuery(r.QueryJson);
            int count = conn.Query<CatalogImage>(BuildAdvancedSql(query).Sql, BuildAdvancedSql(query).Parameters).Count();
            result.Add(new SmartCollection
            {
                Id = r.Id, Name = r.Name, Query = query,
                CreatedAt = DateTime.TryParse(r.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.MinValue,
                ImageCount = count
            });
        }
        return result;
    }

    public SmartCollection CreateSmartCollection(string name, CatalogQuery query)
    {
        using var conn = Open();
        var now = DateTime.UtcNow;
        var json = System.Text.Json.JsonSerializer.Serialize(query, JsonOpts);
        var id = conn.ExecuteScalar<long>(
            "INSERT INTO SmartCollection (Name, QueryJson, CreatedAt) VALUES (@name, @json, @now); SELECT last_insert_rowid();",
            new { name, json, now = now.ToString("o") });
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
        return new SmartCollection { Id = id, Name = name, Query = query, CreatedAt = now };
    }

    public void UpdateSmartCollection(long id, string name, CatalogQuery query)
    {
        using var conn = Open();
        var json = System.Text.Json.JsonSerializer.Serialize(query, JsonOpts);
        conn.Execute("UPDATE SmartCollection SET Name = @name, QueryJson = @json WHERE Id = @id", new { id, name, json });
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSmartCollection(long id)
    {
        using var conn = Open();
        conn.Execute("DELETE FROM SmartCollection WHERE Id = @id", new { id });
        CollectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<CatalogImage> GetSmartCollectionImages(long id)
    {
        using var conn = Open();
        var json = conn.ExecuteScalar<string?>("SELECT QueryJson FROM SmartCollection WHERE Id = @id", new { id });
        if (json == null) return Array.Empty<CatalogImage>();
        var query = DeserializeQuery(json);
        var (sql, parameters) = BuildAdvancedSql(query);
        return conn.Query<CatalogImage>(sql, parameters).ToList();
    }

    private static CatalogQuery DeserializeQuery(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<CatalogQuery>(json, JsonOpts) ?? new CatalogQuery(); }
        catch { return new CatalogQuery(); }
    }
}
