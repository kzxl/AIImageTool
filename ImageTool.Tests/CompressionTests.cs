using System.Collections.Generic;
using System.IO;
using ImageTool.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

/// <summary>Tests cho nén sâu (Squoosh-style): EncoderFactory + TargetSizeEncoder.</summary>
public class CompressionTests
{
    // Ảnh test có gradient + nhiễu nhẹ để JPEG/WebP cho dung lượng phụ thuộc quality thật sự.
    private static Image<Rgba32> MakeColorfulImage(int w = 96, int h = 96)
    {
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte r = (byte)((x * 255) / w);
                byte g = (byte)((y * 255) / h);
                byte b = (byte)(((x + y) * 255) / (w + h));
                // Nhiễu giả lập tần số cao -> JPEG nhạy quality.
                r ^= (byte)((x * 37 + y * 17) & 0x1F);
                img[x, y] = new Rgba32(r, g, b, 255);
            }
        return img;
    }

    private static byte[] EncodeToBytes(Image img, string format, Dictionary<string, string> p)
    {
        var enc = EncoderFactory.Create(format, p);
        using var ms = new MemoryStream();
        img.Save(ms, enc);
        return ms.ToArray();
    }

    [Fact]
    public void Jpeg_LowerQuality_ProducesSmallerFile()
    {
        using var img = MakeColorfulImage();
        long hi = EncodeToBytes(img, "jpg", new() { ["quality"] = "95" }).LongLength;
        long lo = EncodeToBytes(img, "jpg", new() { ["quality"] = "30" }).LongLength;
        Assert.True(lo < hi, $"q30 ({lo}) phải nhỏ hơn q95 ({hi})");
    }

    [Fact]
    public void Jpeg_Subsample420_SmallerThan444()
    {
        using var img = MakeColorfulImage();
        long s444 = EncodeToBytes(img, "jpg", new() { ["quality"] = "85", ["jpegSubsample"] = "444" }).LongLength;
        long s420 = EncodeToBytes(img, "jpg", new() { ["quality"] = "85", ["jpegSubsample"] = "420" }).LongLength;
        Assert.True(s420 <= s444, $"4:2:0 ({s420}) phải ≤ 4:4:4 ({s444})");
    }

    [Fact]
    public void Png_HigherCompressionLevel_NotLargerThanLevel1()
    {
        using var img = MakeColorfulImage();
        long l1 = EncodeToBytes(img, "png", new() { ["pngLevel"] = "1" }).LongLength;
        long l9 = EncodeToBytes(img, "png", new() { ["pngLevel"] = "9" }).LongLength;
        Assert.True(l9 <= l1, $"PNG L9 ({l9}) phải ≤ L1 ({l1})");
    }

    [Fact]
    public void Png_Palette8_SmallerThanTruecolor()
    {
        using var img = MakeColorfulImage();
        long full = EncodeToBytes(img, "png", new() { ["pngLevel"] = "9" }).LongLength;
        long pal = EncodeToBytes(img, "png", new()
        {
            ["pngLevel"] = "9", ["pngColorType"] = "palette", ["pngPaletteColors"] = "64"
        }).LongLength;
        Assert.True(pal < full, $"PNG-8 64 màu ({pal}) phải nhỏ hơn truecolor ({full})");
    }

    [Fact]
    public void Webp_Lossy_SmallerThanLossless()
    {
        using var img = MakeColorfulImage();
        long lossless = EncodeToBytes(img, "webp", new() { ["webpMode"] = "lossless" }).LongLength;
        long lossy = EncodeToBytes(img, "webp", new() { ["webpMode"] = "lossy", ["quality"] = "75" }).LongLength;
        Assert.True(lossy < lossless, $"WebP lossy ({lossy}) phải nhỏ hơn lossless ({lossless})");
    }

    [Fact]
    public void StripMetadata_Flag_AcceptedForAllFormats()
    {
        using var img = MakeColorfulImage(16, 16);
        foreach (var fmt in new[] { "png", "jpg", "webp", "tiff" })
        {
            var data = EncodeToBytes(img, fmt, new() { ["stripMetadata"] = "true" });
            Assert.True(data.Length > 0, $"{fmt} encode rỗng");
        }
    }

    [Fact]
    public void Tiff_Compressed_SmallerThanNone()
    {
        using var img = MakeColorfulImage();
        long none = EncodeToBytes(img, "tiff", new() { ["tiffCompression"] = "none" }).LongLength;
        long deflate = EncodeToBytes(img, "tiff", new() { ["tiffCompression"] = "deflate", ["tiffDeflateLevel"] = "9" }).LongLength;
        Assert.True(deflate < none, $"TIFF deflate ({deflate}) phải nhỏ hơn none ({none})");
    }

    [Fact]
    public void EncoderFactory_UnknownFormat_FallsBackToPng()
    {
        using var img = MakeColorfulImage(8, 8);
        var data = EncodeToBytes(img, "bogus", new());
        // PNG signature: 89 50 4E 47.
        Assert.Equal(0x89, data[0]);
        Assert.Equal((byte)'P', data[1]);
        Assert.Equal((byte)'N', data[2]);
        Assert.Equal((byte)'G', data[3]);
    }

    // --- TargetSizeEncoder ---

    [Fact]
    public void TargetSize_Jpeg_MeetsTarget()
    {
        using var img = MakeColorfulImage(256, 256);
        long full = EncodeToBytes(img, "jpg", new() { ["quality"] = "100" }).LongLength;
        long target = full / 3; // ép nhỏ rõ rệt.
        var res = TargetSizeEncoder.Encode(img, "jpg", new Dictionary<string, string>() { ["quality"] = "100" }, target);
        Assert.True(res.MetTarget);
        Assert.True(res.Bytes <= target, $"{res.Bytes} phải ≤ {target}");
        Assert.InRange(res.Quality, 1, 100);
    }

    [Fact]
    public void TargetSize_ZeroTarget_EncodesOnceAtGivenQuality()
    {
        using var img = MakeColorfulImage(32, 32);
        var res = TargetSizeEncoder.Encode(img, "jpg", new Dictionary<string, string>() { ["quality"] = "70" }, 0);
        Assert.True(res.MetTarget);
        Assert.Equal(70, res.Quality);
        Assert.True(res.Bytes > 0);
    }

    [Fact]
    public void TargetSize_PickHighestQualityUnderTarget()
    {
        using var img = MakeColorfulImage(256, 256);
        long full = EncodeToBytes(img, "jpg", new() { ["quality"] = "100" }).LongLength;
        // Target rộng rãi (90% full) -> nên chọn quality cao, vẫn ≤ target.
        long target = (long)(full * 0.9);
        var res = TargetSizeEncoder.Encode(img, "jpg", new Dictionary<string, string>(), target);
        Assert.True(res.Bytes <= target);
        Assert.True(res.Quality >= 50, $"target rộng nên giữ quality cao, nhận {res.Quality}");
    }

    [Fact]
    public void TargetSize_ImpossiblyTiny_ReturnsLowestQualityNotMet()
    {
        using var img = MakeColorfulImage(256, 256);
        var res = TargetSizeEncoder.Encode(img, "jpg", new Dictionary<string, string>(), 50); // 50 byte = bất khả thi.
        Assert.False(res.MetTarget);
        Assert.Equal(5, res.Quality);
    }

    // --- ExportSizeEstimator (options-aware) ---

    [Fact]
    public void Estimate_JpegSubsample444_LargerThan420()
    {
        long s420 = ExportSizeEstimator.EstimateBytesWithOptions("jpg", 4000, 3000, 0, 85,
            new Dictionary<string, string> { ["jpegSubsample"] = "420" });
        long s444 = ExportSizeEstimator.EstimateBytesWithOptions("jpg", 4000, 3000, 0, 85,
            new Dictionary<string, string> { ["jpegSubsample"] = "444" });
        Assert.True(s444 > s420);
    }

    [Fact]
    public void Estimate_PngPalette_SmallerThanTruecolor()
    {
        long full = ExportSizeEstimator.EstimateBytesWithOptions("png", 2000, 2000, 0, 90, null);
        long pal = ExportSizeEstimator.EstimateBytesWithOptions("png", 2000, 2000, 0, 90,
            new Dictionary<string, string> { ["pngColorType"] = "palette", ["pngPaletteColors"] = "64" });
        Assert.True(pal < full);
    }

    [Fact]
    public void Estimate_WebpLossless_LargerThanLossy()
    {
        long lossy = ExportSizeEstimator.EstimateBytesWithOptions("webp", 2000, 2000, 0, 80,
            new Dictionary<string, string> { ["webpMode"] = "lossy" });
        long lossless = ExportSizeEstimator.EstimateBytesWithOptions("webp", 2000, 2000, 0, 80,
            new Dictionary<string, string> { ["webpMode"] = "lossless" });
        Assert.True(lossless > lossy);
    }

    [Fact]
    public void Estimate_TiffDeflate_SmallerThanNone()
    {
        long none = ExportSizeEstimator.EstimateBytesWithOptions("tiff", 2000, 2000, 0, 90,
            new Dictionary<string, string> { ["tiffCompression"] = "none" });
        long deflate = ExportSizeEstimator.EstimateBytesWithOptions("tiff", 2000, 2000, 0, 90,
            new Dictionary<string, string> { ["tiffCompression"] = "deflate" });
        Assert.True(deflate < none);
    }

    [Fact]
    public void Estimate_NoOptions_EqualsBaseEstimate()
    {
        long baseB = ExportSizeEstimator.EstimateBytes("jpg", 3000, 2000, 0, 85);
        long withNull = ExportSizeEstimator.EstimateBytesWithOptions("jpg", 3000, 2000, 0, 85, null);
        Assert.Equal(baseB, withNull);
    }
}
