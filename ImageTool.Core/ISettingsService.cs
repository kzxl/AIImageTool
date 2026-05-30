namespace ImageTool.Core;

/// <summary>1 preset xuất ảnh (13.5): gói toàn bộ thiết lập Export để lưu/gọi lại nhanh.</summary>
public class ExportPreset
{
    public string Name { get; set; } = "";
    public string Format { get; set; } = "png";        // png/jpg/webp/tiff
    public int Quality { get; set; } = 90;
    public int MaxLongEdge { get; set; }                // 0 = giữ nguyên
    public int ResizePercent { get; set; }              // 0 = không
    public string Pattern { get; set; } = "{name}.{ext}";
    public string OutputSharpen { get; set; } = "none"; // none/screen/low/high
    public string? OutDir { get; set; }
    public string Watermark { get; set; } = "";
    public bool WriteXmp { get; set; }
}

public class AppSettings
{
    public string? LastFolder { get; set; }
    public List<string> RecentFolders { get; set; } = new();
    public int ThumbnailSize { get; set; } = 256;
    public int BatchParallel { get; set; } = 1;
    public int? DefaultGpuId { get; set; }
    public string PerformanceMode { get; set; } = "Safe";
    public List<ExportPreset> ExportPresets { get; set; } = new();
    /// <summary>Giao diện: "Dark" (mặc định) hoặc "Light".</summary>
    public string Theme { get; set; } = "Dark";
}

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
    void AddRecentFolder(string path);
    event EventHandler? Changed;
}
