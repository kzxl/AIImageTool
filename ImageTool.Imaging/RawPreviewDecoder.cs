using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Imaging;

/// <summary>
/// Decoder RAW dựa trên JPEG preview NHÚNG (8.x). Trích JPEG preview full-res do máy ảnh tạo sẵn
/// trong file RAW rồi decode sang LinearImage. Cho phép mở/xem/develop file RAW NGAY mà không cần
/// thư viện native.
///
/// GIỚI HẠN: không phải demosaic sensor thật (12-14 bit) — chỉ là preview 8-bit đã áp picture style.
/// Để có chất lượng RAW thật, 1 plugin LibRaw sẽ đăng ký vào registry và ghi đè các đuôi RAW này.
/// Vì ghi đè theo thứ tự Register, plugin chỉ cần Register sau là tự thay thế (xem ImageDecoderRegistry).
/// </summary>
public sealed class RawPreviewDecoder : IImageDecoder
{
    public string Name => "RAW preview (embedded JPEG)";

    public IReadOnlyCollection<string> SupportedExtensions => RawPreviewExtractor.RawExtensions;

    public DecodedImage Decode(string path)
    {
        var jpeg = RawPreviewExtractor.ExtractLargestJpeg(path);
        if (jpeg == null || jpeg.Length == 0)
            throw new NotSupportedException($"Không tìm thấy JPEG preview nhúng trong RAW: {Path.GetFileName(path)}");

        using var src = Image.Load<Rgba32>(jpeg);
        int orientation = ReadOrientation(src);
        int w = src.Width, h = src.Height;
        var linear = new LinearImage(w, h);
        float[] dst = linear.Pixels;

        src.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int baseOff = y * w * 4;
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 p = ref row[x];
                    int o = baseOff + x * 4;
                    dst[o] = ColorSpace.DecodeByte(p.R);
                    dst[o + 1] = ColorSpace.DecodeByte(p.G);
                    dst[o + 2] = ColorSpace.DecodeByte(p.B);
                    dst[o + 3] = p.A / 255f;
                }
            }
        });

        var meta = new Dictionary<string, string> { ["source"] = "raw-embedded-jpeg" };
        return new DecodedImage
        {
            Image = linear,
            Orientation = orientation,
            IsHighBitDepth = false, // preview JPEG 8-bit
            Metadata = meta
        };
    }

    private static int ReadOrientation(Image src)
    {
        if (src.Metadata?.ExifProfile != null &&
            src.Metadata.ExifProfile.TryGetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, out var ov)
            && ov?.Value != null)
            return ov.Value;
        return 1;
    }
}
