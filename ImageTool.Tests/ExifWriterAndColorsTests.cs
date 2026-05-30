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

    [Fact]
    public void SanitizeProfile_NullSource_ReturnsNull()
    {
        Assert.Null(ExifWriter.SanitizeProfile(null));
    }

    [Fact]
    public void SanitizeProfile_KeepsCameraData_ResetsOrientation()
    {
        var src = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
        src.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Make, "Canon");
        src.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Model, "EOS R5");
        src.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, (ushort)6); // rotated

        var clean = ExifWriter.SanitizeProfile(src);
        Assert.NotNull(clean);
        Assert.True(clean!.TryGetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Make, out var mk));
        Assert.Equal("Canon", mk!.Value);
        Assert.True(clean.TryGetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation, out var ori));
        Assert.Equal((ushort)1, ori!.Value); // reset về Normal
    }

    [Fact]
    public void PreserveExif_CopiesCameraDataToTarget()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_exif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Nguồn có camera EXIF.
            var srcPath = MakeJpeg(dir, "src.jpg");
            ExifWriter.Write(srcPath, new Dictionary<string, string> { ["Make"] = "Nikon", ["Model"] = "Z9" });

            // Ảnh đích "đã render" không có EXIF.
            using var target = new Image<Rgba32>(8, 8, new Rgba32(10, 20, 30, 255));
            Assert.Null(target.Metadata.ExifProfile);

            ExifWriter.PreserveExif(srcPath, target);

            var outPath = Path.Combine(dir, "out.jpg");
            target.SaveAsJpeg(outPath);
            var read = ExifWriter.ReadEditable(outPath);
            Assert.Equal("Nikon", read["Make"]);
            Assert.Equal("Z9", read["Model"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PreserveExif_NoExifSource_NoThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_exif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var srcPath = MakeJpeg(dir, "plain.jpg"); // không EXIF
            using var target = new Image<Rgba32>(8, 8, new Rgba32(1, 2, 3, 255));
            ExifWriter.PreserveExif(srcPath, target); // không ném
            // target vẫn không có EXIF (hoặc null) -> không lỗi.
        }
        finally { Directory.Delete(dir, true); }
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
