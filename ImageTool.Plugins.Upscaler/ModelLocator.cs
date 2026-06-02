using System;
using System.IO;
using System.Linq;

namespace ImageTool.Plugins.Upscaler;

/// <summary>
/// Giải quyết đường dẫn model ONNX một cách BỀN BỈ giữa các layout khác nhau:
/// - Publish (release): Plugins\ImageTool.Plugins.Upscaler\Models\
/// - Build cục bộ (CopyPlugin target): Plugins\Models\ (DLL nằm thẳng trong Plugins\)
/// - Debug fallback: Models\ cạnh exe
/// Trước đây mỗi nơi tự ghép đường dẫn cứng -> build cục bộ không tìm thấy model và
/// pipeline trả ảnh y nguyên IM LẶNG. Helper này gom logic + ghi log khi thất bại.
/// </summary>
public static class ModelLocator
{
    /// <summary>Các thư mục ứng viên chứa model, ưu tiên từ trên xuống.</summary>
    private static string[] CandidateDirs()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return new[]
        {
            Path.Combine(baseDir, "Plugins", "ImageTool.Plugins.Upscaler", "Models"),
            Path.Combine(baseDir, "Plugins", "Models"),
            Path.Combine(baseDir, "Models"),
        };
    }

    /// <summary>
    /// Tìm file model theo tên. Thử các thư mục ứng viên, sau đó quét đệ quy dưới Plugins\.
    /// Trả về null nếu không thấy (đã ghi log).
    /// </summary>
    public static string? Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        foreach (var dir in CandidateDirs())
        {
            var p = Path.Combine(dir, fileName);
            if (File.Exists(p)) return p;
        }

        // Fallback: quét đệ quy dưới Plugins\ (phòng layout lạ).
        var pluginsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (Directory.Exists(pluginsRoot))
        {
            try
            {
                var hit = Directory.EnumerateFiles(pluginsRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null) return hit;
            }
            catch { /* quyền truy cập/symlink lỗi -> bỏ qua */ }
        }

        Log($"Không tìm thấy model '{fileName}'. Đã thử: {string.Join(" ; ", CandidateDirs())}");
        return null;
    }

    /// <summary>Tìm bất kỳ model .onnx nào (dùng cho pipeline export khi không chỉ định model cụ thể).</summary>
    public static string? ResolveAny()
    {
        foreach (var dir in CandidateDirs())
        {
            if (!Directory.Exists(dir)) continue;
            var files = Directory.GetFiles(dir, "*.onnx");
            if (files.Length > 0) return files[0];
        }

        var pluginsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (Directory.Exists(pluginsRoot))
        {
            try
            {
                var hit = Directory.EnumerateFiles(pluginsRoot, "*.onnx", SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null) return hit;
            }
            catch { }
        }

        Log("Không tìm thấy bất kỳ model .onnx nào trong các thư mục Models.");
        return null;
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "upscaler_error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ModelLocator: {message}{Environment.NewLine}");
        }
        catch { /* logger không bao giờ throw */ }
    }
}
