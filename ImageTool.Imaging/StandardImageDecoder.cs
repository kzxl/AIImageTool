using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Imaging;

/// <summary>
/// Decoder cho ảnh nén thông thường (PNG/JPG/JPEG/WebP/BMP/GIF/TGA/TIFF) qua ImageSharp.
/// Đọc sang linear float. Với nguồn 16-bit (TIFF16/PNG16) đọc qua Rgba64 để giữ độ sâu bit,
/// đánh dấu IsHighBitDepth để gợi ý xuất 16-bit.
/// </summary>
public sealed class StandardImageDecoder : IImageDecoder
{
    public string Name => "Standard (PNG/JPG/WebP/BMP/GIF/TGA/TIFF)";

    private static readonly string[] _exts =
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tga", ".tif", ".tiff" };

    public IReadOnlyCollection<string> SupportedExtensions => _exts;

    public DecodedImage Decode(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        // Đoán độ sâu bit: TIFF/PNG có thể 16-bit. Đọc Rgba64 để an toàn (ImageSharp tự scale).
        bool tryHighBit = ext is ".tif" or ".tiff" or ".png";

        if (tryHighBit)
        {
            try { return DecodeHighBit(path); }
            catch { /* fallback 8-bit */ }
        }
        return Decode8Bit(path);
    }

    private static DecodedImage Decode8Bit(string path)
    {
        using var src = Image.Load<Rgba32>(path);
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

        return new DecodedImage
        {
            Image = ExifOrientation.Bake(linear, orientation),
            Orientation = 1, // đã bake vào pixel
            IsHighBitDepth = false,
            Metadata = ReadIccMetadata(src)
        };
    }

    private static DecodedImage DecodeHighBit(string path)
    {
        using var src = Image.Load<Rgba64>(path);
        int orientation = ReadOrientation(src);
        int w = src.Width, h = src.Height;
        var linear = new LinearImage(w, h);
        float[] dst = linear.Pixels;
        const float inv = 1f / 65535f;

        src.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba64> row = accessor.GetRowSpan(y);
                int baseOff = y * w * 4;
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba64 p = ref row[x];
                    int o = baseOff + x * 4;
                    // 16-bit sRGB -> linear (giá trị chuẩn hoá rồi qua công thức sRGB).
                    dst[o] = ColorSpace.SrgbToLinear(p.R * inv);
                    dst[o + 1] = ColorSpace.SrgbToLinear(p.G * inv);
                    dst[o + 2] = ColorSpace.SrgbToLinear(p.B * inv);
                    dst[o + 3] = p.A * inv;
                }
            }
        });

        return new DecodedImage
        {
            Image = ExifOrientation.Bake(linear, orientation),
            Orientation = 1, // đã bake vào pixel
            IsHighBitDepth = true,
            Metadata = ReadIccMetadata(src)
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

    /// <summary>
    /// Đọc ICC profile nhúng (nếu có) -> metadata "iccDesc" (mô tả) + "iccSpace" (gamut đoán được:
    /// sRGB/AdobeRGB/Rec2020/DisplayP3). UI dùng để gợi ý Input Profile. Không áp gì lên pixel.
    /// </summary>
    private static Dictionary<string, string> ReadIccMetadata(Image src)
    {
        var meta = new Dictionary<string, string>();
        try
        {
            var icc = src.Metadata?.IccProfile;
            if (icc == null) return meta;
            byte[] bytes = icc.ToByteArray();
            var desc = IccProfileReader.TryReadDescription(bytes);
            ColorSpaces.Space? space = null;
            if (!string.IsNullOrWhiteSpace(desc))
            {
                meta["iccDesc"] = desc!;
                space = IccProfileReader.GuessSpace(desc);
            }
            // Fallback: nếu tên không khớp gamut quen thuộc, nhận diện theo colorant matrix thật.
            space ??= ColorSpaces.MatchSpace(IccProfileReader.TryReadRgbToXyzD65(bytes) ?? System.Array.Empty<float>());
            if (space.HasValue) meta["iccSpace"] = ColorSpaces.Name(space.Value);
        }
        catch { /* ICC lỗi -> bỏ qua, không metadata */ }
        return meta;
    }
}
