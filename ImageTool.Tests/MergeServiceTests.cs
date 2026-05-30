using System;
using System.Collections.Generic;
using System.IO;
using ImageTool.Imaging;
using ImageTool.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class MergeServiceTests : IDisposable
{
    private readonly string _dir;

    public MergeServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "imgtool_merge_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteSolidPng(string name, byte gray, int w = 16, int h = 16)
    {
        string path = Path.Combine(_dir, name);
        using var img = new Image<Rgba32>(w, h, new Rgba32(gray, gray, gray, 255));
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public void Merge_RequiresTwoImages()
    {
        var decoders = ImageDecoderRegistry.CreateDefault();
        var one = new List<string> { WriteSolidPng("a.png", 128) };
        Assert.Throws<ArgumentException>(() => MergeService.Merge(one, MergeService.Mode.Hdr, decoders));
    }

    [Fact]
    public void Merge_DifferentSizes_Throws()
    {
        var decoders = ImageDecoderRegistry.CreateDefault();
        var paths = new List<string>
        {
            WriteSolidPng("a.png", 100, 16, 16),
            WriteSolidPng("b.png", 100, 8, 8),
        };
        Assert.Throws<InvalidOperationException>(() => MergeService.Merge(paths, MergeService.Mode.Hdr, decoders));
    }

    [Fact]
    public void Merge_Hdr_WritesOutput()
    {
        var decoders = ImageDecoderRegistry.CreateDefault();
        var paths = new List<string>
        {
            WriteSolidPng("dark.png", 40),
            WriteSolidPng("bright.png", 200),
        };
        string outPath = MergeService.Merge(paths, MergeService.Mode.Hdr, decoders);
        Assert.True(File.Exists(outPath));
        Assert.EndsWith("_hdr.png", outPath);
        // đọc lại được + đúng kích thước.
        using var result = Image.Load<Rgba32>(outPath);
        Assert.Equal(16, result.Width);
        Assert.Equal(16, result.Height);
    }

    [Fact]
    public void Merge_FocusStack_WritesOutput()
    {
        var decoders = ImageDecoderRegistry.CreateDefault();
        var paths = new List<string>
        {
            WriteSolidPng("f1.png", 120),
            WriteSolidPng("f2.png", 130),
        };
        string outPath = MergeService.Merge(paths, MergeService.Mode.FocusStack, decoders);
        Assert.True(File.Exists(outPath));
        Assert.Contains("_focusstack", outPath);
    }

    [Fact]
    public void Merge_DoesNotOverwrite()
    {
        var decoders = ImageDecoderRegistry.CreateDefault();
        var paths = new List<string> { WriteSolidPng("x.png", 80), WriteSolidPng("y.png", 160) };
        string out1 = MergeService.Merge(paths, MergeService.Mode.Hdr, decoders);
        string out2 = MergeService.Merge(paths, MergeService.Mode.Hdr, decoders);
        Assert.NotEqual(out1, out2);
        Assert.True(File.Exists(out1) && File.Exists(out2));
    }
}
