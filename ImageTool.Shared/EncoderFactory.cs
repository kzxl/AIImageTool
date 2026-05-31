using System;
using System.Collections.Generic;
using System.Globalization;
using SixLabors.ImageSharp.Compression.Zlib;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace ImageTool.Shared;

/// <summary>
/// Dựng <see cref="IImageEncoder"/> ImageSharp từ tham số nén SÂU (Squoosh-style) cho từng định dạng.
/// Tách khỏi ExportBatchAdapter để test trực tiếp (không cần encode file). Mọi tham số đều có mặc định
/// an toàn (giống encoder mặc định) -> thiếu key vẫn cho ra encoder hợp lệ.
///
/// PNG : compressionLevel 0..9, colorType (rgb/rgba/palette/gray/grayalpha), bitDepth, paletteColors (PNG-8),
///       interlace.
/// JPEG: quality 1..100, subsample (420/422/444), progressive (interleaved off = progressive-ish).
/// WEBP: lossless | lossy | nearlossless, quality, method 0..6 (effort).
/// TIFF: compression (none/lzw/deflate/packbits), predictor, deflate level.
/// Chung: stripMetadata (SkipMetadata).
/// </summary>
public static class EncoderFactory
{
    /// <summary>Tạo encoder cho <paramref name="format"/> ("png"/"jpg"/"jpeg"/"webp"/"tiff") từ params.</summary>
    public static IImageEncoder Create(string format, IReadOnlyDictionary<string, string> p)
    {
        bool strip = B(p, "stripMetadata", false);
        return (format ?? "png").ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => Jpeg(p, strip),
            "webp" => Webp(p, strip),
            "tif" or "tiff" => Tiff(p, strip),
            _ => Png(p, strip),
        };
    }

    private static IImageEncoder Png(IReadOnlyDictionary<string, string> p, bool strip)
    {
        string colorType = S(p, "pngColorType", "auto").ToLowerInvariant();
        PngColorType? ct = colorType switch
        {
            "rgb" => PngColorType.Rgb,
            "rgba" => PngColorType.RgbWithAlpha,
            "gray" => PngColorType.Grayscale,
            "grayalpha" => PngColorType.GrayscaleWithAlpha,
            "palette" => PngColorType.Palette,
            _ => null, // auto
        };

        PngBitDepth? bitDepth = null;
        IQuantizer? quantizer = null;
        if (ct == PngColorType.Palette)
        {
            // PNG-8 (indexed): giảm mạnh dung lượng cho ảnh ít màu / đồ hoạ.
            int colors = Math.Clamp(I(p, "pngPaletteColors", 256), 2, 256);
            bitDepth = colors <= 2 ? PngBitDepth.Bit1
                : colors <= 4 ? PngBitDepth.Bit2
                : colors <= 16 ? PngBitDepth.Bit4
                : PngBitDepth.Bit8;
            quantizer = new WuQuantizer(new QuantizerOptions { MaxColors = colors });
        }

        return new PngEncoder
        {
            CompressionLevel = (PngCompressionLevel)Math.Clamp(I(p, "pngLevel", 6), 0, 9),
            SkipMetadata = strip,
            ColorType = ct,
            BitDepth = bitDepth,
            Quantizer = quantizer,
            InterlaceMethod = B(p, "pngInterlace", false) ? PngInterlaceMode.Adam7 : PngInterlaceMode.None,
        };
    }

    private static IImageEncoder Jpeg(IReadOnlyDictionary<string, string> p, bool strip)
    {
        JpegEncodingColor subsample = S(p, "jpegSubsample", "420").ToLowerInvariant() switch
        {
            "444" => JpegEncodingColor.YCbCrRatio444,
            "422" => JpegEncodingColor.YCbCrRatio422,
            _ => JpegEncodingColor.YCbCrRatio420,
        };
        return new JpegEncoder
        {
            Quality = Math.Clamp(I(p, "quality", 90), 1, 100),
            SkipMetadata = strip,
            ColorType = subsample,
            Interleaved = !B(p, "jpegProgressive", false),
        };
    }

    private static IImageEncoder Webp(IReadOnlyDictionary<string, string> p, bool strip)
    {
        string mode = S(p, "webpMode", "lossy").ToLowerInvariant();
        int quality = Math.Clamp(I(p, "quality", 90), 1, 100);
        int method = Math.Clamp(I(p, "webpMethod", 4), 0, 6);
        bool near = mode == "nearlossless";
        return new WebpEncoder
        {
            Quality = quality,
            Method = (WebpEncodingMethod)method,
            SkipMetadata = strip,
            FileFormat = mode == "lossy" ? WebpFileFormatType.Lossy : WebpFileFormatType.Lossless,
            NearLossless = near,
            NearLosslessQuality = near ? quality : 100,
        };
    }

    private static IImageEncoder Tiff(IReadOnlyDictionary<string, string> p, bool strip)
    {
        string mode = S(p, "tiffCompression", "lzw").ToLowerInvariant();
        TiffCompression comp = mode switch
        {
            "none" => TiffCompression.None,
            "deflate" => TiffCompression.Deflate,
            "packbits" => TiffCompression.PackBits,
            _ => TiffCompression.Lzw,
        };
        // Predictor ngang cải thiện nén cho ảnh ảnh thật (LZW/Deflate).
        bool usePredictor = B(p, "tiffPredictor", true) &&
            (comp == TiffCompression.Lzw || comp == TiffCompression.Deflate);
        return new TiffEncoder
        {
            SkipMetadata = strip,
            Compression = comp,
            CompressionLevel = comp == TiffCompression.Deflate
                ? (DeflateCompressionLevel)Math.Clamp(I(p, "tiffDeflateLevel", 6), 0, 9)
                : null,
            HorizontalPredictor = usePredictor ? TiffPredictor.Horizontal : null,
        };
    }

    // --- helpers parse params (culture-invariant) ---
    private static int I(IReadOnlyDictionary<string, string> p, string k, int def)
        => p.TryGetValue(k, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static bool B(IReadOnlyDictionary<string, string> p, string k, bool def)
        => p.TryGetValue(k, out var s) ? (bool.TryParse(s, out var v) ? v : def) : def;
    private static string S(IReadOnlyDictionary<string, string> p, string k, string def)
        => p.TryGetValue(k, out var s) && !string.IsNullOrEmpty(s) ? s : def;
}
