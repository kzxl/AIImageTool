using System;
using System.IO;

namespace ImageTool.Shared;

/// <summary>
/// Logger nhẹ, an toàn cho lỗi KHÔNG nghiêm trọng bị nuốt (decode/render/save thất bại...).
/// Trước đây nhiều nơi dùng `catch { }` âm thầm khiến không debug được. AppLog ghi 1 dòng gọn
/// vào file (app.log cạnh exe) — bản thân logger không bao giờ throw.
///
/// Khác crash.log (App.xaml.cs, lỗi chí mạng): app.log là cảnh báo/lỗi nhẹ, tiếp tục chạy được.
/// </summary>
public static class AppLog
{
    private static readonly object _lock = new();
    private static string? _path;
    private const long MaxBytes = 2 * 1024 * 1024; // 2MB rồi cắt

    /// <summary>Đường dẫn file log (cạnh exe). Cho phép test ghi đè.</summary>
    public static string Path
    {
        get => _path ??= System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
        set => _path = value;
    }

    public static void Warn(string source, string message) => Write("WARN", source, message, null);
    public static void Error(string source, string message, Exception? ex = null) => Write("ERROR", source, message, ex);

    private static void Write(string level, string source, string message, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {source}: {message}"
                         + (ex != null ? $" | {ex.GetType().Name}: {ex.Message}" : "")
                         + Environment.NewLine;
                // cắt file nếu quá lớn (giữ nhẹ).
                try
                {
                    if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                        File.WriteAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] (log truncated)" + Environment.NewLine);
                }
                catch { }
                File.AppendAllText(Path, line);
            }
        }
        catch { /* logger không bao giờ throw */ }
    }
}
