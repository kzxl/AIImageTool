using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTool.Shared;

/// <summary>
/// Tải ảnh từ URL về thư mục đích (Import from URL). An toàn:
///   - Chỉ chấp nhận http/https (chặn file://, data:, ftp...).
///   - Kiểm tra Content-Type là ảnh + giới hạn dung lượng (chống tải file khổng lồ).
///   - Suy ra tên file an toàn từ URL/Content-Type; không ghi đè (thêm hậu tố).
/// Phần validate URL/tên file thuần logic -> unit test trực tiếp; phần tải mạng tách riêng.
/// </summary>
public static class UrlImageImporter
{
    public const long MaxBytes = 64L * 1024 * 1024; // 64 MB

    private static readonly string[] ImageExtensions =
        { ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff", ".bmp", ".gif" };

    /// <summary>Kiểm tra URL hợp lệ để tải ảnh (chỉ http/https tuyệt đối). Trả uri đã parse.</summary>
    public static bool IsValidImageUrl(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        uri = u;
        return true;
    }

    /// <summary>Suy ra tên file an toàn từ URL + content-type (loại ký tự cấm, đảm bảo có đuôi ảnh).</summary>
    public static string ResolveFileName(Uri uri, string? contentType)
    {
        string name = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(name)) name = "image";

        // Loại query/ký tự không hợp lệ.
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

        string ext = Path.GetExtension(name).ToLowerInvariant();
        if (Array.IndexOf(ImageExtensions, ext) < 0)
        {
            // Không có đuôi ảnh hợp lệ -> suy từ content-type.
            ext = ExtFromContentType(contentType);
            name = Path.GetFileNameWithoutExtension(name) + ext;
        }
        return name;
    }

    /// <summary>Content-Type có phải ảnh không (image/*).</summary>
    public static bool IsImageContentType(string? contentType)
        => !string.IsNullOrEmpty(contentType) &&
           contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string ExtFromContentType(string? ct)
    {
        if (string.IsNullOrEmpty(ct)) return ".jpg";
        ct = ct.ToLowerInvariant();
        if (ct.Contains("png")) return ".png";
        if (ct.Contains("webp")) return ".webp";
        if (ct.Contains("tiff")) return ".tiff";
        if (ct.Contains("bmp")) return ".bmp";
        if (ct.Contains("gif")) return ".gif";
        return ".jpg";
    }

    /// <summary>
    /// Tải ảnh từ <paramref name="url"/> về <paramref name="destFolder"/>. Trả đường dẫn file đã lưu.
    /// Ném <see cref="InvalidOperationException"/> nếu URL không hợp lệ / không phải ảnh / quá lớn.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, string destFolder,
        HttpClient http, CancellationToken ct = default)
    {
        if (!IsValidImageUrl(url, out var uri) || uri == null)
            throw new InvalidOperationException("URL không hợp lệ (chỉ chấp nhận http/https).");

        Directory.CreateDirectory(destFolder);

        using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        string? contentType = resp.Content.Headers.ContentType?.MediaType;
        if (!IsImageContentType(contentType))
            throw new InvalidOperationException($"Nội dung không phải ảnh (Content-Type: {contentType ?? "?"}).");

        long? len = resp.Content.Headers.ContentLength;
        if (len.HasValue && len.Value > MaxBytes)
            throw new InvalidOperationException($"Ảnh quá lớn ({len.Value / (1024 * 1024)} MB > {MaxBytes / (1024 * 1024)} MB).");

        string fileName = ResolveFileName(uri, contentType);
        string outPath = FileNameTokenizer.EnsureUniquePath(Path.Combine(destFolder, fileName));

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
        // Copy có giới hạn dung lượng (chống server không khai báo ContentLength).
        var buffer = new byte[81920];
        long total = 0; int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxBytes)
            {
                dst.Close();
                try { File.Delete(outPath); } catch { }
                throw new InvalidOperationException($"Ảnh vượt giới hạn {MaxBytes / (1024 * 1024)} MB.");
            }
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return outPath;
    }
}
