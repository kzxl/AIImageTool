using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

// Test tầng managed của LibRaw: chuyển buffer demosaic -> LinearImage. Không cần libraw.dll/file RAW.
public class LibRawConverterTests
{
    [Fact]
    public void Pack_8bit_RGB_NormalizesAndSetsAlpha()
    {
        // 2x1, RGB 8-bit: pixel0 = (255,0,0), pixel1 = (0,128,255).
        var data = new byte[] { 255, 0, 0, 0, 128, 255 };
        var img = LibRawImageConverter.Pack(data, 2, 1, colors: 3, bits: 8);
        Assert.Equal(2, img.Width);
        Assert.Equal(1, img.Height);
        // pixel0
        Assert.Equal(1f, img.Pixels[0], 4);
        Assert.Equal(0f, img.Pixels[1], 4);
        Assert.Equal(0f, img.Pixels[2], 4);
        Assert.Equal(1f, img.Pixels[3], 4); // alpha
        // pixel1
        Assert.Equal(0f, img.Pixels[4], 4);
        Assert.Equal(128f / 255f, img.Pixels[5], 4);
        Assert.Equal(1f, img.Pixels[6], 4);
        Assert.Equal(1f, img.Pixels[7], 4);
    }

    [Fact]
    public void Pack_16bit_LittleEndian_Normalizes()
    {
        // 1x1, RGB 16-bit: R=65535 (FF FF), G=0, B=32768 (00 80).
        var data = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x80 };
        var img = LibRawImageConverter.Pack(data, 1, 1, colors: 3, bits: 16);
        Assert.Equal(1f, img.Pixels[0], 4);
        Assert.Equal(0f, img.Pixels[1], 4);
        Assert.Equal(32768f / 65535f, img.Pixels[2], 4);
        Assert.Equal(1f, img.Pixels[3], 4);
    }

    [Fact]
    public void Pack_Grayscale_ReplicatesToRgb()
    {
        // colors=1: 1 kênh -> r=g=b.
        var data = new byte[] { 100, 200 }; // 2 pixel grayscale
        var img = LibRawImageConverter.Pack(data, 2, 1, colors: 1, bits: 8);
        Assert.Equal(100f / 255f, img.Pixels[0], 4);
        Assert.Equal(100f / 255f, img.Pixels[1], 4);
        Assert.Equal(100f / 255f, img.Pixels[2], 4);
        Assert.Equal(200f / 255f, img.Pixels[4], 4);
        Assert.Equal(200f / 255f, img.Pixels[6], 4);
    }

    [Fact]
    public void Pack_4Channels_DropsFourth()
    {
        // colors=4: lấy 3 kênh đầu, bỏ kênh 4.
        var data = new byte[] { 255, 128, 64, 200 };
        var img = LibRawImageConverter.Pack(data, 1, 1, colors: 4, bits: 8);
        Assert.Equal(1f, img.Pixels[0], 4);
        Assert.Equal(128f / 255f, img.Pixels[1], 4);
        Assert.Equal(64f / 255f, img.Pixels[2], 4);
        Assert.Equal(1f, img.Pixels[3], 4); // alpha luôn 1
    }

    [Fact]
    public void Pack_RowStride_CorrectPerRow()
    {
        // 1x2 (cao 2), RGB 8-bit: hàng0=(10,10,10), hàng1=(250,250,250).
        var data = new byte[] { 10, 10, 10, 250, 250, 250 };
        var img = LibRawImageConverter.Pack(data, 1, 2, colors: 3, bits: 8);
        Assert.Equal(10f / 255f, img.Pixels[0], 4);     // (0,0)
        Assert.Equal(250f / 255f, img.Pixels[4], 4);    // (0,1) -> offset 1*1*4
    }

    [Fact]
    public void Pack_BufferTooSmall_Throws()
    {
        var data = new byte[] { 1, 2, 3 }; // cần 2x1x3 = 6
        Assert.Throws<ArgumentException>(() => LibRawImageConverter.Pack(data, 2, 1, 3, 8));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Pack_BadDimensions_Throws(int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LibRawImageConverter.Pack(new byte[16], w, h, 3, 8));
    }

    [Fact]
    public void Pack_BadBits_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LibRawImageConverter.Pack(new byte[16], 1, 1, 3, bits: 12));
    }

    [Fact]
    public void LibRaw_NotAvailable_OnTestMachine_DecoderFallsBack()
    {
        // Máy test không có libraw.dll -> Available=false, registry không đăng ký LibRawDecoder,
        // RAW vẫn decode được qua RawPreviewDecoder. Xác nhận registry vẫn nhận đuôi RAW.
        var reg = ImageDecoderRegistry.CreateDefault();
        Assert.True(reg.CanDecode("photo.cr2"));
        Assert.True(reg.CanDecode("photo.nef"));
    }
}
