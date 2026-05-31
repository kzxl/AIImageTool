using System;
using System.Collections.Generic;
using System.IO;
using ImageTool.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class ContactSheetTests
{
    [Fact]
    public void Layout_EmptyCount_MinHeight()
    {
        var opt = new ContactSheet.Options { Margin = 20 };
        var (cells, h) = ContactSheet.Layout(0, opt);
        Assert.Empty(cells);
        Assert.Equal(40, h);
    }

    [Fact]
    public void Layout_GridPositions()
    {
        var opt = new ContactSheet.Options { Columns = 2, SheetWidth = 440, CellPadding = 10, Margin = 20, ShowFileName = false };
        // innerW = 400, cellOuter = 200, cellImg = 180.
        var (cells, _) = ContactSheet.Layout(4, opt);
        Assert.Equal(4, cells.Count);
        // ô 0: x = 20 + 0 + 10 = 30, y = 20 + 0 + 10 = 30.
        Assert.Equal(30, cells[0].X);
        Assert.Equal(30, cells[0].Y);
        Assert.Equal(180, cells[0].W);
        // ô 1 (cùng hàng, cột 2): x = 20 + 200 + 10 = 230.
        Assert.Equal(230, cells[1].X);
        Assert.Equal(30, cells[1].Y);
        // ô 2 (hàng 2): y khác hàng 1.
        Assert.True(cells[2].Y > cells[0].Y);
        Assert.Equal(30, cells[2].X);
    }

    [Fact]
    public void Layout_RowsRoundUp()
    {
        var opt = new ContactSheet.Options { Columns = 3, ShowFileName = false };
        // 7 ảnh, 3 cột -> 3 hàng.
        var (cells, _) = ContactSheet.Layout(7, opt);
        Assert.Equal(7, cells.Count);
        // ô cuối ở hàng 3 (index 6 -> row 2).
        Assert.True(cells[6].Y > cells[3].Y);
    }

    [Fact]
    public void Layout_SheetHeightGrowsWithLabel()
    {
        var noLabel = new ContactSheet.Options { Columns = 2, ShowFileName = false };
        var withLabel = new ContactSheet.Options { Columns = 2, ShowFileName = true, LabelHeight = 30 };
        var (_, h1) = ContactSheet.Layout(4, noLabel);
        var (_, h2) = ContactSheet.Layout(4, withLabel);
        Assert.True(h2 > h1);
    }

    [Fact]
    public void Render_ComposesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_cs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                var p = Path.Combine(dir, $"img{i}.png");
                using var im = new Image<Rgba32>(60, 40, new Rgba32((byte)(i * 40), 100, 150, 255));
                im.SaveAsPng(p);
                paths.Add(p);
            }
            var outPath = Path.Combine(dir, "sheet.png");
            int drawn = ContactSheet.Render(paths, outPath, new ContactSheet.Options { Columns = 3, SheetWidth = 600, ShowFileName = false });
            Assert.Equal(5, drawn);
            Assert.True(File.Exists(outPath));
            using var sheet = Image.Load<Rgba32>(outPath);
            Assert.Equal(600, sheet.Width);
            Assert.True(sheet.Height > 0);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Render_SkipsBadImages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_cs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "good.png");
            using (var im = new Image<Rgba32>(50, 50, new Rgba32(200, 50, 50, 255))) im.SaveAsPng(good);
            var bad = Path.Combine(dir, "ghost.png"); // không tồn tại
            var outPath = Path.Combine(dir, "sheet.png");
            int drawn = ContactSheet.Render(new[] { good, bad }, outPath, new ContactSheet.Options { Columns = 2, ShowFileName = false });
            Assert.Equal(1, drawn); // bỏ ảnh lỗi
            Assert.True(File.Exists(outPath));
        }
        finally { Directory.Delete(dir, true); }
    }
}
