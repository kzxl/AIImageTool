namespace ImageTool.Core;

public class AppSettings
{
    public string? LastFolder { get; set; }
    public List<string> RecentFolders { get; set; } = new();
    public int ThumbnailSize { get; set; } = 256;
    public int BatchParallel { get; set; } = 1;
    public int? DefaultGpuId { get; set; }
    public string PerformanceMode { get; set; } = "Safe";
}

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
    void AddRecentFolder(string path);
    event EventHandler? Changed;
}
