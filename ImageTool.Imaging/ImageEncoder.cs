using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Imaging;

/// <summary>
/// Xuất LinearImage -> file. Chuyển linear float về sRGB (8-bit hoặc 16-bit) ngay lúc encode.
/// Đây là điểm DUY NHẤT trong pipeline được phép clamp và lượng tử hoá — giữ toàn bộ
/// quá trình edit ở float để không mất chất lượng.
/// </summary>
public static class ImageEncoder
{
    public enum BitDepth { Eight, Sixteen }

    /// <summary>
    /// Lưu ra file. Định dạng suy ra từ đuôi. 16-bit chỉ áp dụng cho PNG (JPEG luôn 8-bit).
    /// quality dùng cho JPEG/WebP (1..100).
    /// </summary>
    public static void Save(LinearImage img, string path, BitDepth depth = BitDepth.Eight, int quality = 95)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

        if (depth == BitDepth.Sixteen && ext == ".png")
        {
            SavePng16(img, path);
            return;
        }

        using var outImg = ToRgba32(img);
        switch (ext)
        {
            case ".jpg":
            case ".jpeg":
                outImg.Save(path, new JpegEncoder { Quality = quality });
                break;
            case ".png":
                outImg.Save(path, new PngEncoder());
                break;
            default:
                outImg.Save(path); // ImageSharp tự suy theo đuôi (webp/bmp/...)
                break;
        }
    }

    /// <summary>Chuyển linear -> Image&lt;Rgba32&gt; 8-bit sRGB (cho preview/encode 8-bit).</summary>
    public static Image<Rgba32> ToRgba32(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var outImg = new Image<Rgba32>(w, h);
        float[] src = img.Pixels;

        outImg.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int baseOff = y * w * 4;
                for (int x = 0; x < row.Length; x++)
                {
                    int o = baseOff + x * 4;
                    ref Rgba32 p = ref row[x];
                    p.R = ColorSpace.EncodeByte(src[o]);
                    p.G = ColorSpace.EncodeByte(src[o + 1]);
                    p.B = ColorSpace.EncodeByte(src[o + 2]);
                    float a = src[o + 3];
                    p.A = (byte)(a <= 0f ? 0 : (a >= 1f ? 255 : (int)(a * 255f + 0.5f)));
                }
            }
        });
        return outImg;
    }

    /// <summary>Chuyển Image&lt;Rgba32&gt; (sRGB 8-bit) -> LinearImage (linear float). Đảo của ToRgba32.</summary>
    public static LinearImage FromRgba32(Image<Rgba32> src)
    {
        int w = src.Width, h = src.Height;
        var img = new LinearImage(w, h);
        float[] dst = img.Pixels;
        src.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int baseOff = y * w * 4;
                for (int x = 0; x < row.Length; x++)
                {
                    int o = baseOff + x * 4;
                    ref Rgba32 p = ref row[x];
                    dst[o] = ColorSpace.DecodeByte(p.R);
                    dst[o + 1] = ColorSpace.DecodeByte(p.G);
                    dst[o + 2] = ColorSpace.DecodeByte(p.B);
                    dst[o + 3] = p.A / 255f;
                }
            }
        });
        return img;
    }

    private static void SavePng16(LinearImage img, string path)
    {
        int w = img.Width, h = img.Height;
        using var outImg = new Image<Rgba64>(w, h);
        float[] src = img.Pixels;

        outImg.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba64> row = accessor.GetRowSpan(y);
                int baseOff = y * w * 4;
                for (int x = 0; x < row.Length; x++)
                {
                    int o = baseOff + x * 4;
                    ref Rgba64 p = ref row[x];
                    p.R = ColorSpace.EncodeUShort(src[o]);
                    p.G = ColorSpace.EncodeUShort(src[o + 1]);
                    p.B = ColorSpace.EncodeUShort(src[o + 2]);
                    float a = src[o + 3];
                    p.A = (ushort)(a <= 0f ? 0 : (a >= 1f ? 65535 : (int)(a * 65535f + 0.5f)));
                }
            }
        });
        outImg.Save(path, new PngEncoder { BitDepth = PngBitDepth.Bit16 });
    }
}
