namespace ImageTool.Core;

public enum ImportMode { AddInPlace = 0, CopyToLibrary = 1 }

public class CatalogImage
{
    public long Id { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime ImportedAt { get; set; }
    public ImportMode ImportMode { get; set; }
    public string? OriginalPath { get; set; }

    public DateTime? DateTaken { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public double? FocalLength { get; set; }
    public double? Aperture { get; set; }
    public string? ShutterSpeed { get; set; }
    public int? Iso { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Orientation { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
}

public class ImageCollection
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int SortOrder { get; set; }
    public int ImageCount { get; set; }
}

public class ImportOptions
{
    public ImportMode Mode { get; set; } = ImportMode.AddInPlace;
    public string? DestinationFolder { get; set; }
    public bool SubfolderByDate { get; set; } = true;
}

public class ImportProgress
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public string? CurrentFile { get; set; }
}

public class ImportCompletedEventArgs : EventArgs
{
    public int ImportedCount { get; }
    public IReadOnlyList<string> ImportedPaths { get; }
    public ImportCompletedEventArgs(int count, IReadOnlyList<string> paths)
    {
        ImportedCount = count;
        ImportedPaths = paths;
    }
}

public enum CatalogSortField { ImportedAt, FileName, DateTaken, Iso, FileSize, Aperture, FocalLength }

/// <summary>
/// Truy vấn tìm kiếm nâng cao (8.4): lọc theo nhiều tiêu chí metadata. Mọi trường null = không lọc.
/// Kết hợp bằng AND. Dùng cho cả search nâng cao lẫn nền cho Smart Collections (8.3).
/// </summary>
public class CatalogQuery
{
    public string? Text { get; set; }            // khớp FileName/FolderPath (LIKE)
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public int? IsoMin { get; set; }
    public int? IsoMax { get; set; }
    public double? ApertureMin { get; set; }
    public double? ApertureMax { get; set; }
    public double? FocalMin { get; set; }
    public double? FocalMax { get; set; }
    public DateTime? DateFrom { get; set; }      // theo DateTaken
    public DateTime? DateTo { get; set; }
    public CatalogSortField SortField { get; set; } = CatalogSortField.ImportedAt;
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Smart Collection (8.3): "bộ sưu tập động" định nghĩa bằng rule (CatalogQuery), không lưu danh
/// sách ảnh cố định mà resolve mỗi lần truy vấn. Query serialize JSON trong DB.
/// </summary>
public class SmartCollection
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public CatalogQuery Query { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public int ImageCount { get; set; }
}

/// <summary>Kết quả đồng bộ thư mục (Sync Folder): số file mới thêm, số file thiếu/đã gỡ.</summary>
public class SyncResult
{
    public int Added { get; set; }
    public int Missing { get; set; }
    public int Removed { get; set; }
    public List<string> MissingPaths { get; set; } = new();
}

public interface ICatalogService
{
    IReadOnlyList<CatalogImage> SearchAdvanced(CatalogQuery query);
    Task<int> ImportAsync(IEnumerable<string> filePaths, ImportOptions options,
                          IProgress<ImportProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ 1 thư mục (kiểu Lightroom "Synchronize Folder"): quét file ảnh trên đĩa, IMPORT các
    /// file CHƯA có trong catalog, và (tuỳ chọn) gỡ các entry mà file không còn trên đĩa.
    /// Trả số file mới thêm + số file thiếu.
    /// </summary>
    Task<SyncResult> SyncFolderAsync(string folderPath, bool recursive, bool removeMissing = false,
                                     IProgress<ImportProgress>? progress = null, CancellationToken ct = default);

    /// <summary>True nếu file đã có trong catalog.</summary>
    bool IsImported(string filePath);
    /// <summary>True nếu folder (đệ quy) chứa ít nhất 1 ảnh đã import vào catalog.</summary>
    bool IsFolderImported(string folderPath);
    /// <summary>Số lượng ảnh trong folder đã import (đệ quy). 0 nếu chưa.</summary>
    int CountImportedInFolder(string folderPath);

    IReadOnlyList<CatalogImage> GetAllImages();
    IReadOnlyList<CatalogImage> GetImagesByFolder(string folderPath);
    IReadOnlyList<CatalogImage> Search(string query);
    CatalogImage? GetImage(string filePath);

    void RemoveFromCatalog(IEnumerable<string> filePaths);

    IReadOnlyList<ImageCollection> GetCollections();
    ImageCollection CreateCollection(string name, string? description = null);
    void RenameCollection(long collectionId, string newName);
    void DeleteCollection(long collectionId);
    void AddToCollection(long collectionId, IEnumerable<string> filePaths);
    void RemoveFromCollection(long collectionId, IEnumerable<string> filePaths);
    IReadOnlyList<CatalogImage> GetCollectionImages(long collectionId);

    // Smart Collections (8.3) — bộ sưu tập động theo rule.
    IReadOnlyList<SmartCollection> GetSmartCollections();
    SmartCollection CreateSmartCollection(string name, CatalogQuery query);
    void UpdateSmartCollection(long id, string name, CatalogQuery query);
    void DeleteSmartCollection(long id);
    IReadOnlyList<CatalogImage> GetSmartCollectionImages(long id);

    event EventHandler<ImportCompletedEventArgs>? ImportCompleted;
    event EventHandler? CollectionsChanged;
}
