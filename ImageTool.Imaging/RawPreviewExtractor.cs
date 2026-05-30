using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

/// <summary>
/// Trích ảnh JPEG preview NHÚNG sẵn trong file RAW (8.x). Mọi RAW (CR2/NEF/ARW/DNG/RAF/RW2...) đều
/// nhúng 1..n ảnh JPEG (thumbnail + preview full-res) trong cấu trúc TIFF/IFD của nó. Ta quét byte
/// tìm các đoạn SOI(FF D8 FF)..EOI(FF D9) và lấy đoạn LỚN NHẤT (≈ preview full-res).
///
/// GIỚI HẠN (trung thực): đây KHÔNG phải demosaic RAW thật — chỉ là JPEG preview do máy ảnh tạo
/// (đã áp picture style của máy). Đủ để xem/duyệt/catalog/develop nhẹ, nhưng không có dữ liệu
/// sensor 12-14 bit. Demosaic thật cần LibRaw (plugin native, đăng ký đè decoder sau).
///
/// Thuần xử lý byte -> unit test được (không cần file RAW thật).
/// </summary>
public static class RawPreviewExtractor
{
    /// <summary>
    /// Tìm đoạn JPEG lớn nhất trong buffer. Trả (offset, length) hoặc null nếu không có.
    /// Quét tìm marker SOI "FF D8 FF" rồi marker EOI "FF D9" tương ứng.
    /// </summary>
    public static (int Offset, int Length)? FindLargestJpeg(byte[] data)
    {
        if (data == null || data.Length < 4) return null;

        (int off, int len)? best = null;
        int i = 0;
        int n = data.Length;
        while (i < n - 2)
        {
            // SOI + bắt đầu marker kế: FF D8 FF
            if (data[i] == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF)
            {
                int end = FindEoi(data, i + 2);
                if (end > i)
                {
                    int len = end - i + 1;
                    if (best == null || len > best.Value.len)
                        best = (i, len);
                    i = end + 1; // nhảy qua JPEG vừa tìm
                    continue;
                }
            }
            i++;
        }
        return best.HasValue ? (best.Value.off, best.Value.len) : null;
    }

    // Tìm EOI (FF D9) từ vị trí start, bỏ qua FF D9 nằm trong dữ liệu nén bằng cách quét tuần tự.
    private static int FindEoi(byte[] data, int start)
    {
        for (int j = start; j < data.Length - 1; j++)
        {
            if (data[j] == 0xFF && data[j + 1] == 0xD9)
                return j + 1;
        }
        return -1;
    }

    /// <summary>Trích bytes JPEG preview lớn nhất từ file RAW. Null nếu không tìm thấy/đọc lỗi.</summary>
    public static byte[]? ExtractLargestJpeg(string path)
    {
        try
        {
            var data = System.IO.File.ReadAllBytes(path);
            var found = FindLargestJpeg(data);
            if (found == null) return null;
            var (off, len) = found.Value;
            var jpeg = new byte[len];
            Array.Copy(data, off, jpeg, len > 0 ? 0 : 0, len);
            return jpeg;
        }
        catch { return null; }
    }

    /// <summary>Đuôi file RAW phổ biến (lowercase, có dấu chấm).</summary>
    public static readonly string[] RawExtensions =
    {
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".rw2", ".orf", ".pef", ".srw", ".raw", ".nrw", ".sr2"
    };

    public static bool IsRawExtension(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return Array.IndexOf(RawExtensions, ext) >= 0;
    }
}
