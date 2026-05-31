using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Shared;

/// <summary>
/// Tạo contact sheet / collage: ghép nhiều ảnh thành 1 file lưới (kèm tên file tuỳ chọn). Phần TÍNH
/// BỐ CỤC LƯỚI (vị trí từng ô) tách riêng <see cref="Layout"/> để unit test; phần vẽ dùng ImageSharp.
/// </summary>
public static class ContactSheet
{
    public sealed class Options
    {
        public int Columns { get; set; } = 4;
        public int SheetWidth { get; set; } = 2000;   // px
        public int CellPadding { get; set; } = 10;     // px quanh mỗi ảnh
        public int Margin { get; set; } = 20;          // px lề ngoài
        public bool ShowFileName { get; set; } = true;
        public int LabelHeight { get; set; } = 22;     // chỗ cho tên file dưới mỗi ô
    }

    /// <summary>1 ô trong lưới (toạ độ pixel trên sheet, chưa gồm padding nội bộ).</summary>
    public readonly record struct Cell(int X, int Y, int W, int H);

    /// <summary>
    /// Tính bố cục lưới: trả danh sách ô + chiều cao tổng của sheet. Mỗi ô vuông cạnh = bề rộng cột
    /// (trừ padding). Thuần toán học -> test được.
    /// </summary>
    public static (IReadOnlyList<Cell> Cells, int SheetHeight) Layout(int count, Options opt)
    {
        var cells = new List<Cell>();
        if (count <= 0) return (cells, opt.Margin * 2);

        int cols = Math.Max(1, opt.Columns);
        int rows = (count + cols - 1) / cols;
        int innerW = opt.SheetWidth - opt.Margin * 2;
        int cellOuter = innerW / cols;                       // bề rộng 1 ô (gồm padding)
        int cellImg = Math.Max(1, cellOuter - opt.CellPadding * 2);
        int labelH = opt.ShowFileName ? opt.LabelHeight : 0;
        int cellOuterH = cellImg + opt.CellPadding * 2 + labelH;

        for (int i = 0; i < count; i++)
        {
            int r = i / cols, c = i % cols;
            int x = opt.Margin + c * cellOuter + opt.CellPadding;
            int y = opt.Margin + r * cellOuterH + opt.CellPadding;
            cells.Add(new Cell(x, y, cellImg, cellImg));
        }
        int sheetH = opt.Margin * 2 + rows * cellOuterH;
        return (cells, sheetH);
    }

    /// <summary>
    /// Render contact sheet ra file. <paramref name="imagePaths"/> = ảnh nguồn (RAW dùng JPEG preview),
    /// <paramref name="outPath"/> = file đích (PNG/JPG theo đuôi). Bỏ qua ảnh lỗi. Trả số ảnh đã ghép.
    /// </summary>
    public static int Render(IReadOnlyList<string> imagePaths, string outPath, Options opt,
        Func<string, Image<Rgba32>?>? loader = null)
    {
        if (imagePaths.Count == 0) return 0;
        var (cells, sheetH) = Layout(imagePaths.Count, opt);

        using var sheet = new Image<Rgba32>(opt.SheetWidth, sheetH, new Rgba32(24, 24, 24, 255));
        Font? font = TryGetFont(12);
        int drawn = 0;

        for (int i = 0; i < imagePaths.Count; i++)
        {
            var cell = cells[i];
            Image<Rgba32>? img = null;
            try
            {
                img = loader != null ? loader(imagePaths[i]) : LoadThumb(imagePaths[i], cell.W, cell.H);
                if (img == null) continue;
                // fit vào ô giữ tỉ lệ.
                img.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(cell.W, cell.H),
                    Mode = ResizeMode.Max,
                }));
                int px = cell.X + (cell.W - img.Width) / 2;
                int py = cell.Y + (cell.H - img.Height) / 2;
                sheet.Mutate(ctx => ctx.DrawImage(img, new Point(px, py), 1f));
                if (opt.ShowFileName && font != null)
                {
                    string name = Path.GetFileName(imagePaths[i]);
                    sheet.Mutate(ctx => ctx.DrawText(name, font, Color.FromRgba(200, 200, 200, 255),
                        new PointF(cell.X, cell.Y + cell.H + 3)));
                }
                drawn++;
            }
            catch { /* ảnh lỗi -> bỏ ô */ }
            finally { img?.Dispose(); }
        }

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        sheet.Save(outPath);
        return drawn;
    }

    private static Image<Rgba32>? LoadThumb(string path, int w, int h)
    {
        // RAW -> JPEG preview nhúng; còn lại load trực tiếp với target size hint.
        if (ImageTool.Imaging.RawPreviewExtractor.IsRawExtension(path))
        {
            var jpeg = ImageTool.Imaging.RawPreviewExtractor.ExtractLargestJpeg(path);
            if (jpeg == null) return null;
            using var ms = new MemoryStream(jpeg);
            var img = Image.Load<Rgba32>(ms);
            try { img.Mutate(x => x.AutoOrient()); } catch { }
            return img;
        }
        var im = Image.Load<Rgba32>(path);
        try { im.Mutate(x => x.AutoOrient()); } catch { }
        return im;
    }

    private static Font? TryGetFont(float size)
    {
        try
        {
            if (SystemFonts.Families.Any())
                return SystemFonts.Families.First().CreateFont(size, FontStyle.Regular);
        }
        catch { }
        return null;
    }
}
