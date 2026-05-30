using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageTool.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class ExifWriterTests
{
    private static string MakeJpeg(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        using var img = new Image<Rgba32>(8, 8, new Rgba32(120, 120, 120, 255));
        img.SaveAsJpeg(path);
        return path;
    }

    [Fact]
    public void Write_And_ReadBack_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_exif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = MakeJpeg(dir, "a.jpg");
            var ok = ExifWriter.Write(path, new Dictionary<string, string>
            {
                ["Artist"] = "Phong Vo",
                ["Copyright"] = "© 2026",
                ["ImageDescription"] = "test desc",
            });
            Assert.True(ok);

            var read = ExifWriter.ReadEditable(path);
            Assert.Equal("Phong Vo", read["Artist"]);
            Assert.Equal("© 2026", read["Copyright"]);
            Assert.Equal("test desc", read["ImageDescription"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_IgnoresUnknownField()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_exif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = MakeJpeg(dir, "b.jpg");
            var ok = ExifWriter.Write(path, new Dictionary<string, string> { ["Bogus"] = "x", ["Make"] = "Canon" });
            Assert.True(ok);
            Assert.Equal("Canon", ExifWriter.ReadEditable(path)["Make"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_MissingFile_ReturnsFalse()
    {
        Assert.False(ExifWriter.Write(@"Z:\nope\ghost.jpg", new Dictionary<string, string> { ["Artist"] = "x" }));
    }

    [Fact]
    public void EditableFields_ContainsExpected()
    {
        Assert.Contains("Artist", ExifWriter.EditableFields);
        Assert.Contains("Copyright", ExifWriter.EditableFields);
    }
}

public class DominantColorsTests
{
    [Fact]
    public void Extract_FindsDominantColor()
    {
        // ảnh chủ yếu đỏ với 1 góc xanh.
        using var img = new Image<Rgba32>(64, 64, new Rgba32(220, 30, 30, 255));
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < 16; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < 16; x++) row[x] = new Rgba32(30, 30, 220, 255);
            }
        });
        var sw = DominantColors.Extract(img, k: 4);
        Assert.NotEmpty(sw);
        // màu chiếm tỉ lệ cao nhất phải nghiêng đỏ.
        var top = sw[0];
        Assert.True(top.R > top.B);
        Assert.True(top.Fraction > 0.5f);
    }

    [Fact]
    public void Extract_FractionsSumToAtMostOne()
    {
        using var img = new Image<Rgba32>(40, 40, new Rgba32(100, 150, 200, 255));
        var sw = DominantColors.Extract(img, k: 5);
        float sum = sw.Sum(s => s.Fraction);
        Assert.InRange(sum, 0.99f, 1.01f);
    }

    [Fact]
    public void Extract_HexFormat()
    {
        using var img = new Image<Rgba32>(16, 16, new Rgba32(255, 128, 0, 255));
        var sw = DominantColors.Extract(img, k: 2);
        Assert.NotEmpty(sw);
        Assert.StartsWith("#", sw[0].Hex);
        Assert.Equal(7, sw[0].Hex.Length);
    }

    [Fact]
    public void Extract_AllWhite_ReturnsEmpty()
    {
        // toàn trắng -> bị loại hết -> rỗng (không crash).
        using var img = new Image<Rgba32>(16, 16, new Rgba32(255, 255, 255, 255));
        var sw = DominantColors.Extract(img, k: 4);
        Assert.Empty(sw);
    }
}
