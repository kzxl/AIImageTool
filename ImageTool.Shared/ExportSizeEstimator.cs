using System;

namespace ImageTool.Shared;

/// <summary>
/// Ước lượng dung lượng file xuất (xấp xỉ) theo định dạng + kích thước + chất lượng — để hiển thị
/// trước khi export. KHÔNG encode thật; dùng hệ số bit-per-pixel kinh nghiệm. Sai số chấp nhận được
/// cho mục đích xem nhanh. Thuần toán học -> unit test trực tiếp.
/// </summary>
public static class ExportSizeEstimator
{
    /// <summary>
    /// Ước lượng số BYTE cho 1 ảnh đầu ra. <paramref name="srcW"/>×<paramref name="srcH"/> = kích thước
    /// nguồn; <paramref name="maxLongEdge"/> &gt; 0 thì scale theo cạnh dài. quality 1..100 cho jpg/webp.
    /// </summary>
    public static long EstimateBytes(string format, int srcW, int srcH, int maxLongEdge, int quality)
    {
        if (srcW <= 0 || srcH <= 0) return 0;

        // Kích thước đầu ra sau resize theo cạnh dài.
        long w = srcW, h = srcH;
        if (maxLongEdge > 0)
        {
            long longEdge = Math.Max(srcW, srcH);
            if (longEdge > maxLongEdge)
            {
                double s = (double)maxLongEdge / longEdge;
                w = Math.Max(1, (long)Math.Round(srcW * s));
                h = Math.Max(1, (long)Math.Round(srcH * s));
            }
        }
        long pixels = w * h;
        int q = Math.Clamp(quality, 1, 100);

        double bpp = format.ToLowerInvariant() switch
        {
            // JPEG: bpp tăng phi tuyến theo quality (q90 ~1.5bpp, q50 ~0.5bpp, q100 ~3bpp).
            "jpg" or "jpeg" => 0.25 + 2.75 * Math.Pow(q / 100.0, 2.2),
            // WebP lossy: nén tốt hơn JPEG ~30%.
            "webp" => (0.25 + 2.75 * Math.Pow(q / 100.0, 2.2)) * 0.7,
            // PNG: lossless ~ phụ thuộc nội dung; trung bình ~6 bpp cho ảnh ảnh thật.
            "png" => 6.0,
            // TIFF không nén ~24 bpp (8-bit RGB).
            "tif" or "tiff" => 24.0,
            _ => 6.0,
        };
        return (long)(pixels * bpp / 8.0);
    }

    /// <summary>Định dạng dung lượng người đọc (B/KB/MB).</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }
}
