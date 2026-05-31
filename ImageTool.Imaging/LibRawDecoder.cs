using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

/// <summary>
/// Decoder RAW DEMOSAIC THẬT qua LibRaw (D5.1/D5.2): xuất dữ liệu sensor 12-14 bit đã demosaic
/// (output linear-gamma + sRGB primaries 16-bit) thay vì JPEG preview nhúng. Chỉ hoạt động khi
/// <see cref="LibRawNative.Available"/> (có libraw.dll); nếu native lỗi với 1 file cụ thể, tự FALLBACK
/// nội bộ sang <see cref="RawPreviewDecoder"/> (JPEG preview) thay vì ném — ảnh luôn mở được.
///
/// Đăng ký SAU RawPreviewDecoder trong registry để ĐÈ đuôi RAW khi khả dụng.
/// </summary>
public sealed class LibRawDecoder : IImageDecoder
{
    private readonly RawPreviewDecoder _fallback = new();

    public string Name => "RAW (LibRaw demosaic)";

    public IReadOnlyCollection<string> SupportedExtensions => RawPreviewExtractor.RawExtensions;

    /// <summary>True nếu native LibRaw nạp được (để registry quyết định có đăng ký không).</summary>
    public static bool Available => LibRawNative.Available;

    public DecodedImage Decode(string path)
    {
        var raw = LibRawNative.TryDecode(path);
        if (raw == null)
            return _fallback.Decode(path); // native lỗi với file này -> JPEG preview

        // LibRaw xuất linear-gamma + sRGB primaries -> chuẩn hoá thẳng, KHÔNG áp lại sRGB curve.
        var linear = LibRawImageConverter.Pack(raw.Pixels, raw.Width, raw.Height, raw.Colors, raw.Bits);

        return new DecodedImage
        {
            Image = linear,
            Orientation = 1, // LibRaw đã xoay đúng pixel theo flip metadata
            IsHighBitDepth = raw.Bits >= 16,
            Metadata = new Dictionary<string, string> { ["source"] = "libraw-demosaic" },
        };
    }
}
