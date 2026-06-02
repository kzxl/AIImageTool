using System;
using System.Collections.Generic;
using System.IO;
using ImageTool.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageTool.Tests;

public class PrintModuleTests
{
    // ---- Math thuần (test được, không cần file) ----

    [Fact]
    public void MmToPx_300Dpi_OneInch_Is300()
    {
        Assert.Equal(300, PrintModule.MmToPx(25.4, 300));
        Assert.Equal(150, PrintModule.MmToPx(25.4, 150));
    }

    [Fact]
    public void A4_Portrait_300Dpi_HasExpectedPixelSize()
    {
        var layout = PrintModule.Layout(new PrintModule.Options
        {
            Paper = PrintModule.PaperSize.A4,
            Orientation = PrintModule.Orientation.Portrait,
            Dpi = 300, Rows = 1, Columns = 1, MarginMm = 0
        });
        // A4 = 210 x 297 mm @ 300dpi -> 2480 x 3508 px (chuẩn in ấn).
        Assert.Equal(2480, layout.PageWidth);
        Assert.Equal(3508, layout.PageHeight);
    }

    [Fact]
    public void Landscape_SwapsDimensions()
    {
        var p = PrintModule.Layout(new PrintModule.Options
        {
            Paper = PrintModule.PaperSize.A4, Orientation = PrintModule.Orientation.Portrait, Dpi = 300, MarginMm = 0
        });
        var l = PrintModule.Layout(new PrintModule.Options
        {
            Paper = PrintModule.PaperSize.A4, Orientation = PrintModule.Orientation.Landscape, Dpi = 300, MarginMm = 0
        });
        Assert.Equal(p.PageWidth, l.PageHeight);
        Assert.Equal(p.PageHeight, l.PageWidth);
    }

    [Fact]
    public void Grid_ProducesRowsTimesColumnsCells()
    {
        var layout = PrintModule.Layout(new PrintModule.Options
        {
            Paper = PrintModule.PaperSize.A4, Dpi = 150, Rows = 2, Columns = 3
        });
        Assert.Equal(6, layout.Cells.Count);
    }

    [Fact]
    public void Grid_CellsRespectMarginAndGap()
    {
        var opt = new PrintModule.Options
        {
            Paper = PrintModule.PaperSize.A4, Dpi = 300, Rows = 2, Columns = 2, MarginMm = 10, GapMm = 5
        };
        var layout = PrintModule.Layout(opt);
        int margin = PrintModule.MmToPx(10, 300);
        // ô đầu tiên bắt đầu đúng tại lề.
        Assert.Equal(margin, layout.Cells[0].X);
        Assert.Equal(margin, layout.Cells[0].Y);
        // ô cột 2 nằm bên phải ô cột 1 (có gap).
        Assert.True(layout.Cells[1].X > layout.Cells[0].X + layout.Cells[0].W);
        // ô hàng 2 nằm dưới hàng 1.
        Assert.True(layout.Cells[2].Y > layout.Cells[0].Y);
    }

    [Fact]
    public void Cells_StayWithinPage()
    {
        var layout = PrintModule.Layout(new PrintModule.Options
        {
            Paper = PrintModule.PaperSize.Letter, Dpi = 200, Rows = 3, Columns = 3, MarginMm = 12, GapMm = 4
        });
        foreach (var c in layout.Cells)
        {
            Assert.True(c.X >= 0 && c.Y >= 0);
            Assert.True(c.X + c.W <= layout.PageWidth);
            Assert.True(c.Y + c.H <= layout.PageHeight);
        }
    }

    // ---- Render (file tạm) ----

    [Fact]
    public void Render_SinglePhoto_ComposesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_print_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "a.png");
            using (var im = new Image<Rgba32>(800, 600, new Rgba32(50, 120, 200, 255))) im.SaveAsPng(src);

            var outPath = Path.Combine(dir, "print.png");
            // DPI thấp để file test nhẹ.
            int placed = PrintModule.Render(new[] { src }, outPath,
                new PrintModule.Options { Paper = PrintModule.PaperSize.Photo4x6, Dpi = 100, Rows = 1, Columns = 1 });

            Assert.Equal(1, placed);
            Assert.True(File.Exists(outPath));
            using var page = Image.Load<Rgba32>(outPath);
            // Photo4x6 landscape-agnostic: kích thước > 0 và khớp layout.
            var layout = PrintModule.Layout(new PrintModule.Options { Paper = PrintModule.PaperSize.Photo4x6, Dpi = 100 });
            Assert.Equal(layout.PageWidth, page.Width);
            Assert.Equal(layout.PageHeight, page.Height);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Render_NUp_PlacesMultiple_AndSkipsExtra()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_print_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                var p = Path.Combine(dir, $"img{i}.png");
                using var im = new Image<Rgba32>(200, 200, new Rgba32((byte)(i * 30), 100, 100, 255));
                im.SaveAsPng(p);
                paths.Add(p);
            }
            var outPath = Path.Combine(dir, "print.png");
            // 2x2 = 4 ô; 6 ảnh -> chỉ đặt 4, bỏ 2.
            int placed = PrintModule.Render(paths, outPath,
                new PrintModule.Options { Paper = PrintModule.PaperSize.A5, Dpi = 100, Rows = 2, Columns = 2 });
            Assert.Equal(4, placed);
            Assert.True(File.Exists(outPath));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Render_SkipsBadImage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_print_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "good.png");
            using (var im = new Image<Rgba32>(100, 100, new Rgba32(200, 50, 50, 255))) im.SaveAsPng(good);
            var bad = Path.Combine(dir, "missing.png");
            var outPath = Path.Combine(dir, "print.png");
            int placed = PrintModule.Render(new[] { good, bad }, outPath,
                new PrintModule.Options { Paper = PrintModule.PaperSize.A5, Dpi = 80, Rows = 1, Columns = 2 });
            Assert.Equal(1, placed);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- Multi-page (#6) ----

    [Fact]
    public void PageCount_RoundsUp()
    {
        var opt = new PrintModule.Options { Rows = 2, Columns = 2 }; // 4 ô/trang
        Assert.Equal(0, PrintModule.PageCount(0, opt));
        Assert.Equal(1, PrintModule.PageCount(4, opt));
        Assert.Equal(2, PrintModule.PageCount(5, opt));
        Assert.Equal(3, PrintModule.PageCount(9, opt));
        Assert.Equal(4, PrintModule.CellsPerPage(opt));
    }

    [Fact]
    public void RenderPages_SplitsAcrossPages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_print_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                var p = Path.Combine(dir, $"img{i}.png");
                using var im = new Image<Rgba32>(120, 120, new Rgba32((byte)(i * 40), 80, 160, 255));
                im.SaveAsPng(p);
                paths.Add(p);
            }
            var template = Path.Combine(dir, "sheet.png");
            // 2x2 = 4 ô/trang; 5 ảnh -> 2 trang.
            var written = PrintModule.RenderPages(paths, template,
                new PrintModule.Options { Paper = PrintModule.PaperSize.A5, Dpi = 80, Rows = 2, Columns = 2 });
            Assert.Equal(2, written.Count);
            foreach (var f in written) Assert.True(File.Exists(f));
            // tên file có hậu tố trang khi >1 trang.
            Assert.Contains(written, f => f.Contains("_p01"));
            Assert.Contains(written, f => f.Contains("_p02"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RenderPages_SinglePage_KeepsName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_print_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "a.png");
            using (var im = new Image<Rgba32>(100, 100, new Rgba32(10, 200, 90, 255))) im.SaveAsPng(p);
            var template = Path.Combine(dir, "sheet.png");
            var written = PrintModule.RenderPages(new[] { p }, template,
                new PrintModule.Options { Paper = PrintModule.PaperSize.Photo4x6, Dpi = 80, Rows = 1, Columns = 1 });
            Assert.Single(written);
            Assert.Equal(template, written[0]); // 1 trang -> giữ nguyên tên, không thêm hậu tố.
        }
        finally { Directory.Delete(dir, true); }
    }
}
