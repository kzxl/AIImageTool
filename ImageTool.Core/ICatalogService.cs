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

public interface ICatalogService
{
    Task<int> ImportAsync(IEnumerable<string> filePaths, ImportOptions options,
                          IProgress<ImportProgress>? progress = null, CancellationToken ct = default);
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

    event EventHandler<ImportCompletedEventArgs>? ImportCompleted;
    event EventHandler? CollectionsChanged;
}
