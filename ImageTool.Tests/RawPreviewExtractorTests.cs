using System;
using System.IO;
using ImageTool.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class RawPreviewExtractorTests
{
    // Tạo bytes JPEG hợp lệ (SOI..EOI) bằng ImageSharp.
    private static byte[] MakeJpeg(int w, int h, byte r, byte g, byte b)
    {
        using var img = new Image<Rgba32>(w, h, new Rgba32(r, g, b, 255));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    // Mô phỏng 1 file RAW: vài byte rác + thumbnail JPEG nhỏ + rác + preview JPEG lớn + rác.
    private static byte[] BuildFakeRaw(byte[] small, byte[] large)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x11, 0x22 }); // TIFF-ish header rác
        ms.Write(small);
        ms.Write(new byte[] { 0x00, 0x01, 0x02, 0x03 });
        ms.Write(large);
        ms.Write(new byte[] { 0xAA, 0xBB });
        return ms.ToArray();
    }

    [Fact]
    public void FindLargestJpeg_PicksBiggerOne()
    {
        var small = MakeJpeg(16, 16, 200, 50, 50);
        var large = MakeJpeg(96, 96, 50, 200, 50);
        var raw = BuildFakeRaw(small, large);

        var found = RawPreviewExtractor.FindLargestJpeg(raw);
        Assert.NotNull(found);
        var (off, len) = found!.Value;
        // đoạn tìm được phải khớp kích thước JPEG lớn.
        Assert.Equal(large.Length, len);
        // và decode lại được thành ảnh 96x96.
        var slice = new byte[len];
        Array.Copy(raw, off, slice, 0, len);
        using var img = Image.Load<Rgba32>(slice);
        Assert.Equal(96, img.Width);
    }

    [Fact]
    public void FindLargestJpeg_NoJpeg_ReturnsNull()
    {
        var junk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        Assert.Null(RawPreviewExtractor.FindLargestJpeg(junk));
    }

    [Fact]
    public void FindLargestJpeg_EmptyOrTiny_Null()
    {
        Assert.Null(RawPreviewExtractor.FindLargestJpeg(Array.Empty<byte>()));
        Assert.Null(RawPreviewExtractor.FindLargestJpeg(new byte[] { 0xFF }));
    }

    [Fact]
    public void IsRawExtension_DetectsCommonRaw()
    {
        Assert.True(RawPreviewExtractor.IsRawExtension("photo.CR2"));
        Assert.True(RawPreviewExtractor.IsRawExtension("x.nef"));
        Assert.True(RawPreviewExtractor.IsRawExtension("y.arw"));
        Assert.True(RawPreviewExtractor.IsRawExtension("z.dng"));
        Assert.False(RawPreviewExtractor.IsRawExtension("a.jpg"));
        Assert.False(RawPreviewExtractor.IsRawExtension("b.png"));
    }

    [Fact]
    public void ExtractLargestJpeg_FromFile_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_raw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var small = MakeJpeg(20, 20, 10, 10, 200);
            var large = MakeJpeg(80, 60, 200, 200, 10);
            var rawPath = Path.Combine(dir, "fake.cr2");
            File.WriteAllBytes(rawPath, BuildFakeRaw(small, large));

            var jpeg = RawPreviewExtractor.ExtractLargestJpeg(rawPath);
            Assert.NotNull(jpeg);
            using var img = Image.Load<Rgba32>(jpeg!);
            Assert.Equal(80, img.Width);
            Assert.Equal(60, img.Height);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RawPreviewDecoder_DecodesEmbeddedJpeg()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_raw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var preview = MakeJpeg(64, 48, 120, 80, 40);
            var rawPath = Path.Combine(dir, "shot.nef");
            File.WriteAllBytes(rawPath, BuildFakeRaw(MakeJpeg(8, 8, 0, 0, 0), preview));

            var decoder = new RawPreviewDecoder();
            var decoded = decoder.Decode(rawPath);
            Assert.Equal(64, decoded.Image.Width);
            Assert.Equal(48, decoded.Image.Height);
            Assert.Equal("raw-embedded-jpeg", decoded.Metadata["source"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Registry_HasRawDecoder()
    {
        var reg = ImageDecoderRegistry.CreateDefault();
        Assert.True(reg.CanDecode("x.cr2"));
        Assert.True(reg.CanDecode("y.dng"));
        Assert.True(reg.CanDecode("z.jpg")); // standard vẫn còn
    }
}
