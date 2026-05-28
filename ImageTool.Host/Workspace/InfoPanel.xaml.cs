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

                var bmp = RenderHistogram(r, g, b, 256, 100);

                Dispatcher.BeginInvoke(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    Exif.Clear();
                    foreach (var row in rows) Exif.Add(row);
                    imgHistogram.Source = bmp;
                    txtHistEmpty.Visibility = Visibility.Collapsed;
                });
            }
            catch { }
        }, ct);
    }

    private static BitmapSource RenderHistogram(int[] r, int[] g, int[] b, int w, int h)
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
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
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
