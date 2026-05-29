using System;
using System.Collections.Generic;
using System.IO;

namespace ImageTool.Imaging;

/// <summary>
/// Kết quả decode: ảnh ở linear light + metadata thô (orientation, ICC, EXIF...).
/// Metadata để dạng dictionary để decoder RAW sau này nhồi thêm (ISO, WB-as-shot...)
/// mà không phải đổi chữ ký.
/// </summary>
public sealed class DecodedImage
{
    public required LinearImage Image { get; init; }

    /// <summary>EXIF orientation gốc (1..8). Pipeline có thể xoay ở op Geometry.</summary>
    public int Orientation { get; init; } = 1;

    /// <summary>True nếu nguồn có nhiều hơn 8-bit/kênh (RAW, TIFF16) — gợi ý nên xuất 16-bit.</summary>
    public bool IsHighBitDepth { get; init; }

    /// <summary>Metadata thô tuỳ decoder (camera, ISO, WB nhân hệ số...). Có thể rỗng.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Bộ giải mã 1 họ định dạng -> LinearImage. Cài StandardImageDecoder cho ảnh nén
/// thông thường; RAW sẽ là 1 implementation khác đăng ký vào registry sau này.
/// </summary>
public interface IImageDecoder
{
    /// <summary>Tên hiển thị (vd "Standard (PNG/JPG/WebP)", "RAW (LibRaw)").</summary>
    string Name { get; }

    /// <summary>Đuôi file hỗ trợ, đã chuẩn hoá lowercase, gồm dấu chấm (".jpg").</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>Decode 1 file thành ảnh linear + metadata. Ném nếu không đọc được.</summary>
    DecodedImage Decode(string path);
}

/// <summary>
/// Sổ đăng ký decoder. Host gọi Register() lúc khởi động (built-in + plugin RAW).
/// Pipeline chỉ hỏi registry "ai decode được file này" — hoàn toàn không biết RAW hay JPG.
/// Đây chính là điểm "viết sẵn để đấu nối RAW sau" mà không sửa lõi.
/// </summary>
public sealed class ImageDecoderRegistry
{
    private readonly List<IImageDecoder> _decoders = new();
    private readonly Dictionary<string, IImageDecoder> _byExt = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IImageDecoder decoder)
    {
        if (decoder == null) throw new ArgumentNullException(nameof(decoder));
        _decoders.Add(decoder);
        foreach (var ext in decoder.SupportedExtensions)
        {
            // Decoder đăng ký sau ghi đè ext trùng (cho phép plugin RAW thay thế built-in nếu muốn).
            _byExt[ext] = decoder;
        }
    }

    public bool CanDecode(string path) => _byExt.ContainsKey(Path.GetExtension(path));

    public IReadOnlyList<IImageDecoder> Decoders => _decoders;

    /// <summary>Danh sách mọi đuôi file decode được (cho dialog Open Filter).</summary>
    public IEnumerable<string> SupportedExtensions => _byExt.Keys;

    public DecodedImage Decode(string path)
    {
        var ext = Path.GetExtension(path);
        if (!_byExt.TryGetValue(ext, out var decoder))
            throw new NotSupportedException($"Không có decoder cho định dạng '{ext}'. File: {path}");
        return decoder.Decode(path);
    }

    /// <summary>Registry mặc định đã nạp sẵn decoder chuẩn.</summary>
    public static ImageDecoderRegistry CreateDefault()
    {
        var reg = new ImageDecoderRegistry();
        reg.Register(new StandardImageDecoder());
        return reg;
    }
}
