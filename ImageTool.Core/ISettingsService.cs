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
    /// <summary>Giữ EXIF gốc (camera/lens/ngày/GPS) trên file xuất. Mặc định true.</summary>
    public bool CopyExif { get; set; } = true;

    // --- Nén nâng cao (Squoosh-style) ---
    /// <summary>Dung lượng mục tiêu KB (0 = bỏ qua, chỉ jpg/webp lossy).</summary>
    public int TargetKB { get; set; }
    /// <summary>Xoá toàn bộ metadata khi xuất (web).</summary>
    public bool StripMetadata { get; set; }
    /// <summary>JPEG chroma subsampling: "420"/"422"/"444".</summary>
    public string JpegSubsample { get; set; } = "420";
    public bool JpegProgressive { get; set; }
    /// <summary>PNG deflate level 0..9.</summary>
    public int PngLevel { get; set; } = 6;
    /// <summary>PNG palette (PNG-8) + số màu (2..256). 0 = truecolor.</summary>
    public int PngPaletteColors { get; set; }
    /// <summary>WebP mode: "lossy"/"lossless"/"nearlossless".</summary>
    public string WebpMode { get; set; } = "lossy";
    /// <summary>WebP effort/method 0..6.</summary>
    public int WebpMethod { get; set; } = 4;
    /// <summary>TIFF compression: "lzw"/"deflate"/"packbits"/"none".</summary>
    public string TiffCompression { get; set; } = "lzw";
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

    /// <summary>Từ điển tag do người dùng định nghĩa (phân cấp, chuẩn hoá "A/B/C"). Dùng cho gợi ý/autocomplete.</summary>
    public List<string> TagDictionary { get; set; } = new();
    /// <summary>Tag dùng gần đây nhất (mới nhất đứng đầu), giới hạn số lượng.</summary>
    public List<string> RecentTags { get; set; } = new();

    /// <summary>Bề rộng cột panel trái (Workspace Browser), px. 0 = dùng mặc định.</summary>
    public double LeftPanelWidth { get; set; }
    /// <summary>Bề rộng cột panel phải (Tools), px. 0 = dùng mặc định.</summary>
    public double RightPanelWidth { get; set; }
}

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
    void AddRecentFolder(string path);
    /// <summary>Ghi nhận các tag vừa dùng (chuẩn hoá, đưa lên đầu RecentTags, gộp vào TagDictionary).</summary>
    void AddRecentTags(IEnumerable<string> tags);
    event EventHandler? Changed;
}
