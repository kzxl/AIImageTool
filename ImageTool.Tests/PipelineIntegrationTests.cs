using System;
using System.Collections.Generic;
using System.IO;
using ImageTool.Core;
using ImageTool.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

/// <summary>
/// Test integration đầu-cuối: ghi PNG thật -> decode qua ImageDecoderRegistry -> replay ops qua
/// EditPipeline -> encode lại bằng ImageEncoder -> đọc lại pixel xác nhận. Đây là đường đi thực
/// của Export (ExportBatchAdapter) nên bắt được lỗi tích hợp decode/encode mà unit test op không thấy.
/// </summary>
public class PipelineIntegrationTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_pipe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WritePng(string dir, string name, byte r, byte g, byte b, int w = 32, int h = 32)
    {
        var path = Path.Combine(dir, name);
        using var img = new Image<Rgba32>(w, h, new Rgba32(r, g, b, 255));
        img.SaveAsPng(path);
        return path;
    }

    private static (byte R, byte G, byte B) ReadCenterPixel(string path)
    {
        using var img = Image.Load<Rgba32>(path);
        var p = img[img.Width / 2, img.Height / 2];
        return (p.R, p.G, p.B);
    }

    [Fact]
    public void DecodeEditEncode_Exposure_Brightens()
    {
        var dir = TempDir();
        try
        {
            var src = WritePng(dir, "src.png", 64, 64, 64);
            var decoders = ImageDecoderRegistry.CreateDefault();
            var reg = EditOpRegistry.CreateDefault();
            var pipeline = new EditPipeline(reg);

            var decoded = decoders.Decode(src);
            var ops = new List<EditOperation>
            {
                new() { OpType = DevelopBasicOp.Type, Params = new() { ["exposure"] = "1" } } // +1 EV ~ x2
            };
            var rendered = pipeline.Render(decoded.Image, ops);
            var outPath = Path.Combine(dir, "out.png");
            ImageEncoder.Save(rendered, outPath);

            Assert.True(File.Exists(outPath));
            var before = ReadCenterPixel(src);
            var after = ReadCenterPixel(outPath);
            Assert.True(after.R > before.R + 20, $"after {after.R} phải sáng hơn before {before.R}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DecodeEditEncode_Invert_Reverses()
    {
        var dir = TempDir();
        try
        {
            var src = WritePng(dir, "s.png", 30, 30, 30);
            var decoders = ImageDecoderRegistry.CreateDefault();
            var reg = EditOpRegistry.CreateDefault();
            var pipeline = new EditPipeline(reg);

            var decoded = decoders.Decode(src);
            var ops = new List<EditOperation>
            {
                new() { OpType = InvertOp.Type, Params = new() { ["enabled"] = "true" } }
            };
            var rendered = pipeline.Render(decoded.Image, ops);
            var outPath = Path.Combine(dir, "o.png");
            ImageEncoder.Save(rendered, outPath);

            var after = ReadCenterPixel(outPath);
            // 30 -> đảo trong sRGB ~ 225.
            Assert.True(after.R > 200, $"đảo 30 phải ~225, được {after.R}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DecodeEditEncode_Crop_ChangesDimensions()
    {
        var dir = TempDir();
        try
        {
            var src = WritePng(dir, "c.png", 100, 120, 140, 40, 40);
            var decoders = ImageDecoderRegistry.CreateDefault();
            var reg = EditOpRegistry.CreateDefault();
            var pipeline = new EditPipeline(reg);

            var decoded = decoders.Decode(src);
            var ops = new List<EditOperation>
            {
                new() { OpType = CropOp.Type, Params = new() { ["x"] = "0.25", ["y"] = "0.25", ["w"] = "0.5", ["h"] = "0.5" } }
            };
            var rendered = pipeline.Render(decoded.Image, ops);
            var outPath = Path.Combine(dir, "co.png");
            ImageEncoder.Save(rendered, outPath);

            using var outImg = Image.Load<Rgba32>(outPath);
            Assert.Equal(20, outImg.Width);
            Assert.Equal(20, outImg.Height);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DecodeEditEncode_MultiOpChain_NoCorruption()
    {
        var dir = TempDir();
        try
        {
            var src = WritePng(dir, "m.png", 90, 110, 130, 48, 48);
            var decoders = ImageDecoderRegistry.CreateDefault();
            var reg = EditOpRegistry.CreateDefault();
            var pipeline = new EditPipeline(reg);

            var decoded = decoders.Decode(src);
            var ops = new List<EditOperation>
            {
                new() { OpType = DevelopBasicOp.Type, Params = new() { ["contrast"] = "0.3", ["saturation"] = "0.2" } },
                new() { OpType = VignetteOp.Type, Params = new() { ["amount"] = "-0.4" } },
                new() { OpType = SharpenOp.Type, Params = new() { ["amount"] = "0.5" } },
            };
            var rendered = pipeline.Render(decoded.Image, ops);
            var outPath = Path.Combine(dir, "mo.png");
            ImageEncoder.Save(rendered, outPath);

            using var outImg = Image.Load<Rgba32>(outPath);
            Assert.Equal(48, outImg.Width);
            // pixel tâm vẫn hợp lệ (không NaN/đen hoàn toàn do lỗi chain).
            var c = ReadCenterPixel(outPath);
            Assert.True(c.R + c.G + c.B > 0);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Encode16Bit_Png_Loads()
    {
        var dir = TempDir();
        try
        {
            var src = WritePng(dir, "h.png", 200, 100, 50);
            var decoders = ImageDecoderRegistry.CreateDefault();
            var decoded = decoders.Decode(src);
            var outPath = Path.Combine(dir, "h16.png");
            ImageEncoder.Save(decoded.Image, outPath, ImageEncoder.BitDepth.Sixteen);

            Assert.True(File.Exists(outPath));
            using var outImg = Image.Load<Rgba64>(outPath);
            Assert.Equal(32, outImg.Width);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExportBatchAdapter_BakesEdits_FromHistory()
    {
        var dir = TempDir();
        try
        {
            var src = WritePng(dir, "e.png", 50, 50, 50);
            var history = new ImageTool.Shared.HistoryService();
            history.Push(src, new EditOperation
            {
                PluginId = "Develop", OpType = DevelopBasicOp.Type,
                Params = new() { ["exposure"] = "1.5" }
            });

            var adapter = new ImageTool.Shared.ExportBatchAdapter(history);
            var outDir = Path.Combine(dir, "out");
            var job = new BatchJob
            {
                PluginId = ImageTool.Shared.ExportBatchAdapter.Plugin,
                OpType = ImageTool.Shared.ExportBatchAdapter.OpExport,
                InputPath = src,
                Params = new() { ["format"] = "png", ["outDir"] = outDir, ["pattern"] = "{name}.{ext}" }
            };
            adapter.RunJobAsync(job, new Progress<int>(), default).GetAwaiter().GetResult();

            Assert.False(string.IsNullOrEmpty(job.OutputPath));
            Assert.True(File.Exists(job.OutputPath));
            var before = ReadCenterPixel(src);
            var after = ReadCenterPixel(job.OutputPath!);
            Assert.True(after.R > before.R, "export phải bake +1.5 EV (sáng hơn gốc)");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
