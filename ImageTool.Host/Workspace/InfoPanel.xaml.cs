using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Host.Workspace;

public partial class InfoPanel : UserControl
{
    private IWorkspaceService? _workspace;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ExifRow> Exif { get; } = new();

    public InfoPanel()
    {
        InitializeComponent();
        icExif.ItemsSource = Exif;
    }

    public void Bind(IWorkspaceService ws)
    {
        _workspace = ws;
        _workspace.ActiveImageChanged += (s, e) => Refresh(e.CurrentPath);
    }

    private void Refresh(string? path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Dispatcher.BeginInvoke(() =>
        {
            Exif.Clear();
            imgHistogram.Source = null;
            txtHistEmpty.Visibility = Visibility.Visible;
        });

        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        Task.Run(() =>
        {
            try
            {
                using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(path);

                // EXIF
                var rows = new List<ExifRow>();
                var fi = new FileInfo(path);
                rows.Add(new ExifRow("File", fi.Name));
                rows.Add(new ExifRow("Size", $"{fi.Length / 1024.0:N0} KB"));
                rows.Add(new ExifRow("Pixels", $"{img.Width} x {img.Height}"));
                rows.Add(new ExifRow("Modified", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")));

                if (img.Metadata.ExifProfile != null)
                {
                    foreach (var v in img.Metadata.ExifProfile.Values)
                    {
                        if (ct.IsCancellationRequested) return;
                        var val = v.GetValue()?.ToString();
                        if (string.IsNullOrEmpty(val)) continue;
                        if (val.Length > 80) val = val.Substring(0, 80) + "…";
                        rows.Add(new ExifRow(v.Tag.ToString() ?? "", val));
                    }
                }

                // Histogram (downscale rồi tính)
                using var small = img.Clone(c => c.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(256, 256),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                    Sampler = KnownResamplers.Box
                }));

                int[] r = new int[256], g = new int[256], b = new int[256];
                small.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            r[row[x].R]++;
                            g[row[x].G]++;
                            b[row[x].B]++;
                        }
                    }
                });

                if (ct.IsCancellationRequested) return;

                // Cảnh báo clip: % pixel cháy sáng (>=254 cả 3 kênh) / mất chi tiết tối (<=1).
                long total = 0; for (int i = 0; i < 256; i++) total += r[i];
                long hiClip = r[255] + r[254] + g[255] + g[254] + b[255] + b[254];
                long loClip = r[0] + r[1] + g[0] + g[1] + b[0] + b[1];
                double hiPct = total > 0 ? hiClip / (3.0 * total) * 100.0 : 0;
                double loPct = total > 0 ? loClip / (3.0 * total) * 100.0 : 0;
                bool hiWarn = hiPct > 0.5, loWarn = loPct > 0.5;

                var bmp = RenderHistogram(r, g, b, 256, 100, hiWarn, loWarn);

                Dispatcher.BeginInvoke(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    Exif.Clear();
                    foreach (var row in rows) Exif.Add(row);
                    if (hiWarn) Exif.Insert(0, new ExifRow("⚠ Highlight clip", $"{hiPct:0.0}%"));
                    if (loWarn) Exif.Insert(hiWarn ? 1 : 0, new ExifRow("⚠ Shadow clip", $"{loPct:0.0}%"));
                    imgHistogram.Source = bmp;
                    txtHistEmpty.Visibility = Visibility.Collapsed;
                });
            }
            catch { }
        }, ct);
    }

    private static BitmapSource RenderHistogram(int[] r, int[] g, int[] b, int w, int h, bool hiClip = false, bool loClip = false)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 15, 15)),
                null, new Rect(0, 0, w, h));
            int max = 0;
            for (int i = 0; i < 256; i++) max = Math.Max(max, Math.Max(r[i], Math.Max(g[i], b[i])));
            if (max <= 0) max = 1;
            DrawChannel(dc, r, max, w, h, System.Windows.Media.Color.FromArgb(160, 240, 80, 80));
            DrawChannel(dc, g, max, w, h, System.Windows.Media.Color.FromArgb(160, 80, 220, 80));
            DrawChannel(dc, b, max, w, h, System.Windows.Media.Color.FromArgb(160, 80, 140, 240));

            // Marker tam giác cảnh báo clip: góc trái-trên (shadow), phải-trên (highlight).
            if (loClip)
                dc.DrawGeometry(new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 160, 255)), null,
                    Triangle(0, 0, 10));
            if (hiClip)
                dc.DrawGeometry(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 90, 90)), null,
                    Triangle(w - 10, 0, 10));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static Geometry Triangle(double x, double y, double s)
    {
        var fig = new PathFigure { StartPoint = new System.Windows.Point(x, y), IsClosed = true };
        fig.Segments.Add(new LineSegment(new System.Windows.Point(x + s, y), true));
        fig.Segments.Add(new LineSegment(new System.Windows.Point(x, y + s), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private static void DrawChannel(DrawingContext dc, int[] data, int max, int w, int h, System.Windows.Media.Color c)
    {
        var brush = new SolidColorBrush(c);
        double bw = (double)w / 256;
        for (int i = 0; i < 256; i++)
        {
            double bh = (double)data[i] / max * h;
            dc.DrawRectangle(brush, null, new Rect(i * bw, h - bh, bw, bh));
        }
    }
}

public record ExifRow(string Name, string Value);
