using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImageTool.Shared;

/// <summary>
/// Theo dõi thư mục (#2) — phát hiện ảnh MỚI thêm vào 1 folder (tethering-lite / auto-import). Dùng
/// <see cref="FileSystemWatcher"/>, lọc theo đuôi ảnh, gom event trùng (FSW hay bắn nhiều lần cho 1 file).
/// Raise <see cref="ImageAdded"/> khi file ổn định. Phần lọc đuôi tách riêng -> unit test trực tiếp.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff", ".bmp", ".gif",
      ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef", ".srw" };

    private FileSystemWatcher? _fsw;
    private readonly object _lock = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bắn khi 1 ảnh MỚI xuất hiện trong folder theo dõi (đường dẫn đầy đủ).</summary>
    public event EventHandler<string>? ImageAdded;

    public string? Folder { get; private set; }
    public bool IsWatching => _fsw != null;

    /// <summary>Có phải đuôi ảnh hỗ trợ không (dùng cho lọc + test).</summary>
    public static bool IsImageFile(string path)
        => !string.IsNullOrEmpty(path) && ImageExt.Contains(Path.GetExtension(path));

    /// <summary>Bắt đầu theo dõi 1 folder. Dừng folder cũ nếu đang theo dõi.</summary>
    public void Start(string folder)
    {
        Stop();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        Folder = folder;

        // Ghi nhận file hiện có để không coi là "mới".
        lock (_lock)
        {
            _seen.Clear();
            foreach (var f in Directory.EnumerateFiles(folder))
                if (IsImageFile(f)) _seen.Add(f);
        }

        _fsw = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _fsw.Created += OnChanged;
        _fsw.Renamed += OnRenamed;
    }

    public void Stop()
    {
        if (_fsw != null)
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Created -= OnChanged;
            _fsw.Renamed -= OnRenamed;
            _fsw.Dispose();
            _fsw = null;
        }
        Folder = null;
    }

    private void OnRenamed(object sender, RenamedEventArgs e) => Consider(e.FullPath);
    private void OnChanged(object sender, FileSystemEventArgs e) => Consider(e.FullPath);

    private void Consider(string fullPath)
    {
        if (!IsImageFile(fullPath)) return;
        lock (_lock)
        {
            if (!_seen.Add(fullPath)) return; // đã thấy -> bỏ qua trùng
        }
        // FSW bắn ngay khi file bắt đầu ghi -> chờ file ổn định rồi mới báo.
        System.Threading.Tasks.Task.Run(async () =>
        {
            if (await WaitFileReady(fullPath))
                ImageAdded?.Invoke(this, fullPath);
        });
    }

    /// <summary>Chờ file ghi xong (kích thước ổn định + mở đọc được). Trả false nếu quá hạn.</summary>
    private static async System.Threading.Tasks.Task<bool> WaitFileReady(string path, int timeoutMs = 10000)
    {
        long lastLen = -1; int stable = 0;
        for (int waited = 0; waited < timeoutMs; waited += 250)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) { await System.Threading.Tasks.Task.Delay(250); continue; }
                long len = fi.Length;
                if (len > 0 && len == lastLen) { if (++stable >= 2) return CanOpen(path); }
                else stable = 0;
                lastLen = len;
            }
            catch { /* đang bị khoá -> chờ */ }
            await System.Threading.Tasks.Task.Delay(250);
        }
        return false;
    }

    private static bool CanOpen(string path)
    {
        try { using var s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
        catch { return false; }
    }

    public void Dispose() => Stop();
}
