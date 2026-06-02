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
/// Print module: dựng 1 ảnh raster sẵn-sàng-in theo khổ giấy + DPI + lề, đặt 1 hoặc nhiều ảnh
/// (N-up grid) vào trang. Theo đúng mẫu ContactSheet: phần TÍNH BỐ CỤC (PageLayout) tách riêng để
/// unit test thuần toán học; phần vẽ dùng ImageSharp. Khác ContactSheet ở chỗ kích thước trang tính
/// theo đơn vị vật lý (mm/inch -> pixel theo DPI) nên file in ra đúng tỉ lệ giấy thật.
/// </summary>
public static class PrintModule
{
    /// <summary>Khổ giấy chuẩn (kích thước theo mm, khổ dọc/portrait).</summary>
    public enum PaperSize { A4, A3, A5, Letter, Legal, Photo4x6, Photo5x7, Photo8x10 }

    public enum Orientation { Portrait, Landscape }

    /// <summary>Cách đặt ảnh vào ô: Fit = vừa khít giữ tỉ lệ (có viền), Fill = lấp đầy ô (crop bớt).</summary>
    public enum FitMode { Fit, Fill }

    public sealed class Options
    {
        public PaperSize Paper { get; set; } = PaperSize.A4;
        public Orientation Orientation { get; set; } = Orientation.Portrait;
        public int Dpi { get; set; } = 300;
        public double MarginMm { get; set; } = 10.0;   // lề ngoài
        public double GapMm { get; set; } = 5.0;        // khoảng cách giữa các ô
        public int Rows { get; set; } = 1;
        public int Columns { get; set; } = 1;
        public FitMode Fit { get; set; } = FitMode.Fit;
        public bool ShowFileName { get; set; } = false;
        public Rgba32 Background { get; set; } = new Rgba32(255, 255, 255, 255);
    }

    /// <summary>1 ô ảnh trên trang (pixel).</summary>
    public readonly record struct Cell(int X, int Y, int W, int H);

    /// <summary>Bố cục trang: kích thước trang (px) + danh sách ô. Thuần toán học -> test được.</summary>
    public readonly record struct PageLayout(int PageWidth, int PageHeight, IReadOnlyList<Cell> Cells);

    /// <summary>Kích thước giấy theo mm (W x H) ở chiều dọc/portrait.</summary>
    public static (double WidthMm, double HeightMm) PaperMm(PaperSize size) => size switch
    {
        PaperSize.A3 => (297.0, 420.0),
        PaperSize.A4 => (210.0, 297.0),
        PaperSize.A5 => (148.0, 210.0),
        PaperSize.Letter => (215.9, 279.4),
        PaperSize.Legal => (215.9, 355.6),
        PaperSize.Photo4x6 => (101.6, 152.4),
        PaperSize.Photo5x7 => (127.0, 177.8),
        PaperSize.Photo8x10 => (203.2, 254.0),
        _ => (210.0, 297.0)
    };

    /// <summary>mm -> pixel theo DPI (1 inch = 25.4 mm).</summary>
    public static int MmToPx(double mm, int dpi) => (int)Math.Round(mm / 25.4 * dpi);

    /// <summary>
    /// Tính bố cục trang: trang theo khổ giấy + DPI + orientation, chia lưới Rows x Columns ô bằng nhau
    /// trừ lề ngoài + khoảng cách giữa ô. Trả page size px + ô.
    /// </summary>
    public static PageLayout Layout(Options opt)
    {
        var (wMm, hMm) = PaperMm(opt.Paper);
        if (opt.Orientation == Orientation.Landscape) (wMm, hMm) = (hMm, wMm);

        int dpi = Math.Max(1, opt.Dpi);
        int pageW = Math.Max(1, MmToPx(wMm, dpi));
        int pageH = Math.Max(1, MmToPx(hMm, dpi));

        int rows = Math.Max(1, opt.Rows);
        int cols = Math.Max(1, opt.Columns);
        int margin = Math.Max(0, MmToPx(opt.MarginMm, dpi));
        int gap = Math.Max(0, MmToPx(opt.GapMm, dpi));

        int innerW = Math.Max(1, pageW - margin * 2 - gap * (cols - 1));
        int innerH = Math.Max(1, pageH - margin * 2 - gap * (rows - 1));
        int cellW = Math.Max(1, innerW / cols);
        int cellH = Math.Max(1, innerH / rows);

        var cells = new List<Cell>(rows * cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int x = margin + c * (cellW + gap);
                int y = margin + r * (cellH + gap);
                cells.Add(new Cell(x, y, cellW, cellH));
            }
        }
        return new PageLayout(pageW, pageH, cells);
    }

    /// <summary>
    /// Render trang in ra file. imagePaths xếp lần lượt vào các ô (theo Rows x Columns). Ảnh thừa bị bỏ
    /// (1 trang); thiếu thì để ô trống. Trả số ảnh đã đặt. loader cho phép test/override cách nạp ảnh.
    /// </summary>
    public static int Render(IReadOnlyList<string> imagePaths, string outPath, Options opt,
        Func<string, Image<Rgba32>?>? loader = null)
    {
        var layout = Layout(opt);
        using var page = new Image<Rgba32>(layout.PageWidth, layout.PageHeight, opt.Background);
        Font? font = opt.ShowFileName ? TryGetFont(Math.Max(10, opt.Dpi / 24)) : null;

        int n = Math.Min(imagePaths.Count, layout.Cells.Count);
        int placed = 0;
        for (int i = 0; i < n; i++)
        {
            var cell = layout.Cells[i];
            Image<Rgba32>? img = null;
            try
            {
                img = loader != null ? loader(imagePaths[i]) : LoadImage(imagePaths[i]);
                if (img == null) continue;

                int labelH = (font != null) ? (int)(font.Size * 1.6) : 0;
                int imgAreaH = Math.Max(1, cell.H - labelH);

                if (opt.Fit == FitMode.Fill)
                {
                    img.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(cell.W, imgAreaH),
                        Mode = ResizeMode.Crop
                    }));
                }
                else
                {
                    img.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(cell.W, imgAreaH),
                        Mode = ResizeMode.Max
                    }));
                }

                int px = cell.X + (cell.W - img.Width) / 2;
                int py = cell.Y + (imgAreaH - img.Height) / 2;
                var local = img;
                page.Mutate(ctx => ctx.DrawImage(local, new Point(px, py), 1f));

                if (font != null)
                {
                    string name = Path.GetFileName(imagePaths[i]);
                    page.Mutate(ctx => ctx.DrawText(name, font, Color.FromRgba(40, 40, 40, 255),
                        new PointF(cell.X, cell.Y + imgAreaH + 2)));
                }
                placed++;
            }
            catch { /* ảnh lỗi -> bỏ ô */ }
            finally { img?.Dispose(); }
        }

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        page.Save(outPath);
        return placed;
    }

    /// <summary>Số ô trên 1 trang (Rows × Columns, tối thiểu 1).</summary>
    public static int CellsPerPage(Options opt) => Math.Max(1, opt.Rows) * Math.Max(1, opt.Columns);

    /// <summary>Số trang cần để in <paramref name="imageCount"/> ảnh với lưới hiện tại.</summary>
    public static int PageCount(int imageCount, Options opt)
    {
        if (imageCount <= 0) return 0;
        int per = CellsPerPage(opt);
        return (imageCount + per - 1) / per;
    }

    /// <summary>
    /// Render NHIỀU trang khi số ảnh vượt số ô/trang: chia ảnh theo từng trang rồi gọi <see cref="Render"/>.
    /// outPathTemplate có thể chứa "{page}" để chèn số trang (vd "print_{page}.png"); nếu không có,
    /// tự thêm hậu tố "_p01", "_p02"... trước phần đuôi. Trả danh sách đường dẫn file đã ghi.
    /// </summary>
    public static IReadOnlyList<string> RenderPages(IReadOnlyList<string> imagePaths, string outPathTemplate,
        Options opt, Func<string, Image<Rgba32>?>? loader = null)
    {
        var written = new List<string>();
        if (imagePaths.Count == 0) return written;

        int per = CellsPerPage(opt);
        int pages = PageCount(imagePaths.Count, opt);
        for (int pg = 0; pg < pages; pg++)
        {
            var slice = imagePaths.Skip(pg * per).Take(per).ToList();
            string outPath = PagePath(outPathTemplate, pg + 1, pages);
            int placed = Render(slice, outPath, opt, loader);
            if (placed > 0) written.Add(outPath);
        }
        return written;
    }

    private static string PagePath(string template, int pageNo, int totalPages)
    {
        // 1 trang + không có token -> giữ nguyên tên (không thêm hậu tố thừa).
        if (template.Contains("{page}"))
            return template.Replace("{page}", pageNo.ToString("D2"));
        if (totalPages <= 1) return template;

        var dir = Path.GetDirectoryName(template) ?? "";
        var name = Path.GetFileNameWithoutExtension(template);
        var ext = Path.GetExtension(template);
        return Path.Combine(dir, $"{name}_p{pageNo:D2}{ext}");
    }

    private static Image<Rgba32>? LoadImage(string path)
    {
        if (ImageTool.Imaging.RawPreviewExtractor.IsRawExtension(path))
        {
            var jpeg = ImageTool.Imaging.RawPreviewExtractor.ExtractLargestJpeg(path);
            if (jpeg == null) return null;
            using var ms = new MemoryStream(jpeg);
            var im = Image.Load<Rgba32>(ms);
            try { im.Mutate(x => x.AutoOrient()); } catch { }
            return im;
        }
        var img = Image.Load<Rgba32>(path);
        try { img.Mutate(x => x.AutoOrient()); } catch { }
        return img;
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
