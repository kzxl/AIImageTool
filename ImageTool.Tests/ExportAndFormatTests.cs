using System.Collections.Generic;
using System.IO;
using ImageTool.Core;
using ImageTool.Imaging;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class ExportAndFormatTests : IDisposable
{
    private readonly string _dir;

    public ExportAndFormatTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "imgtool_fmt_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Encoder16Bit_Decoder16Bit_RoundTrip()
    {
        // Tạo ảnh linear, lưu PNG 16-bit, decode lại -> high bit depth + giá trị gần đúng.
        var img = new LinearImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = ColorSpace.SrgbToLinear(0.4f);
            img.Pixels[i + 1] = ColorSpace.SrgbToLinear(0.6f);
            img.Pixels[i + 2] = ColorSpace.SrgbToLinear(0.8f);
            img.Pixels[i + 3] = 1f;
        }
        string path = Path.Combine(_dir, "test16.png");
        ImageEncoder.Save(img, path, ImageEncoder.BitDepth.Sixteen);

        var decoded = new StandardImageDecoder().Decode(path);
        Assert.True(decoded.IsHighBitDepth);
        // round-trip linear ~ giữ giá trị (sai số nhỏ).
        Assert.Equal(ColorSpace.SrgbToLinear(0.4f), decoded.Image.Pixels[0], 2);
        Assert.Equal(ColorSpace.SrgbToLinear(0.8f), decoded.Image.Pixels[2], 2);
    }

    [Fact]
    public void TiffExtension_IsDecodable()
    {
        var dec = new StandardImageDecoder();
        Assert.Contains(".tiff", dec.SupportedExtensions);
        Assert.Contains(".tif", dec.SupportedExtensions);
    }

    [Fact]
    public void XmpSidecar_WritesValidFile()
    {
        string img = Path.Combine(_dir, "photo.jpg");
        File.WriteAllText(img, "x");
        var ops = new List<EditOperation>
        {
            new EditOperation { PluginId = "Develop", OpType = "DevelopBasic", Params = new() { ["exposure"] = "1.5" } }
        };
        XmpSidecar.Write(img, ops, 1);

        string xmpPath = XmpSidecar.PathFor(img);
        Assert.True(File.Exists(xmpPath));
        string content = File.ReadAllText(xmpPath);
        Assert.Contains("imgtool:OpType", content);
        Assert.Contains("DevelopBasic", content);
        Assert.Contains("1.5", content);
    }

    [Fact]
    public void XmpSidecar_RespectsPointer()
    {
        string img = Path.Combine(_dir, "p2.jpg");
        File.WriteAllText(img, "x");
        var ops = new List<EditOperation>
        {
            new EditOperation { PluginId = "Develop", OpType = "OpA", Params = new() },
            new EditOperation { PluginId = "Develop", OpType = "OpB", Params = new() },
        };
        XmpSidecar.Write(img, ops, 1); // chỉ op đầu
        string content = File.ReadAllText(XmpSidecar.PathFor(img));
        Assert.Contains("OpA", content);
        Assert.DoesNotContain("OpB", content);
    }
}
