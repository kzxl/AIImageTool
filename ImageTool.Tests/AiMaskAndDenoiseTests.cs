using System;
using System.Collections.Generic;
using System.IO;
using ImageTool.Core;
using ImageTool.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class RasterMaskTests
{
    private static LinearImage Solid(float v, int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // Ghi 1 mask PNG xám: nửa trái = 0 (đen), nửa phải = 255 (trắng).
    private static string WriteHalfMask(string dir, int w = 8, int h = 8)
    {
        var path = Path.Combine(dir, "mask.png");
        using var img = new Image<L8>(w, h);
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = new L8((byte)(x >= w / 2 ? 255 : 0));
            }
        });
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public void Generate_ResamplesMask()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_rmask_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mp = WriteHalfMask(dir, 8, 8);
            var rm = new RasterMask { MaskFile = mp };
            var m = rm.Generate(32, 32);
            // cột trái ~0, cột phải ~1 (đã resample lên 32x32).
            Assert.True(m[0] < 0.1f);
            int rightIdx = 0 * 32 + 31;
            Assert.True(m[rightIdx] > 0.9f);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Generate_Invert_Flips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_rmask_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mp = WriteHalfMask(dir, 8, 8);
            var rm = new RasterMask { MaskFile = mp, Invert = true };
            var m = rm.Generate(16, 16);
            Assert.True(m[0] > 0.9f);            // trái (gốc 0) -> 1 sau invert
            Assert.True(m[15] < 0.1f);           // phải (gốc 1) -> 0
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Generate_MissingFile_EmptyMask()
    {
        var rm = new RasterMask { MaskFile = @"Z:\nope\x.png" };
        var m = rm.Generate(8, 8);
        foreach (var v in m) Assert.Equal(0f, v);
    }

    [Fact]
    public void ViaMaskedOp_AppliesOnlyWhereMaskWhite()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_rmask_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mp = WriteHalfMask(dir, 16, 16);
            var reg = EditOpRegistry.CreateDefault();
            Assert.True(reg.Has(MaskedOp.Type));
            var p = new Dictionary<string, string>
            {
                ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
                ["mask"] = RasterMask.Type, ["maskFile"] = mp, ["invert"] = "false",
            };
            var op = reg.Create(MaskedOp.Type, p);
            Assert.NotNull(op);

            var img = Solid(0.25f, 16, 16);
            op!.Apply(img, 1f);
            // cột trái (mask 0) gần như không đổi; cột phải (mask 1) sáng lên.
            Assert.InRange(img.Pixels[0], 0.24f, 0.30f);
            int rightP = (0 * 16 + 15) * 4;
            Assert.True(img.Pixels[rightP] > 0.4f);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class AiDenoiseOpTests
{
    private static LinearImage Solid(float v) { var i = new LinearImage(4, 4); for (int k = 0; k < i.Pixels.Length; k += 4) { i.Pixels[k] = v; i.Pixels[k + 1] = v; i.Pixels[k + 2] = v; i.Pixels[k + 3] = 1f; } return i; }

    [Fact]
    public void Identity_WhenStrengthZero()
    {
        Assert.True(new AiDenoiseOp { Strength = 0 }.IsIdentity);
    }

    [Fact]
    public void NoProcessor_IsNoOp()
    {
        AiOpHost.DenoiseProcessor = null;
        var img = Solid(0.5f);
        new AiDenoiseOp { Strength = 1f, PreviewSkip = false }.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f); // không đổi vì chưa có processor
    }

    [Fact]
    public void PreviewSkip_SkipsWhenScaledDown()
    {
        bool called = false;
        AiOpHost.DenoiseProcessor = (im, s, sc) => called = true;
        try
        {
            var img = Solid(0.5f);
            new AiDenoiseOp { Strength = 1f, PreviewSkip = true }.Apply(img, 0.5f); // proxy
            Assert.False(called);
            new AiDenoiseOp { Strength = 1f, PreviewSkip = true }.Apply(img, 1f);   // full-res
            Assert.True(called);
        }
        finally { AiOpHost.DenoiseProcessor = null; }
    }

    [Fact]
    public void Processor_ReceivesStrength()
    {
        float got = -1;
        AiOpHost.DenoiseProcessor = (im, s, sc) => got = s;
        try
        {
            new AiDenoiseOp { Strength = 0.7f, PreviewSkip = false }.Apply(Solid(0.5f), 1f);
            Assert.Equal(0.7f, got, 3);
        }
        finally { AiOpHost.DenoiseProcessor = null; }
    }

    [Fact]
    public void Registered_InRegistry()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(AiDenoiseOp.Type));
    }
}
