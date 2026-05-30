using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private IImageMetaService? _meta;
    private ISettingsService? _settings;
    private CancellationTokenSource? _cts;
    private string? _currentPath;

    public ObservableCollection<ExifRow> Exif { get; } = new();
    public ObservableCollection<ColorSwatchVm> Colors { get; } = new();
    public ObservableCollection<ColorSwatchVm> Suggestions { get; } = new();
    public ObservableCollection<KeywordVm> Keywords { get; } = new();
    public ObservableCollection<KeywordVm> KeywordSuggestions { get; } = new();

    public InfoPanel()
    {
        InitializeComponent();
        icExif.ItemsSource = Exif;
        icColors.ItemsSource = Colors;
        icSuggest.ItemsSource = Suggestions;
        icKeywords.ItemsSource = Keywords;
        icKeywordSuggest.ItemsSource = KeywordSuggestions;
    }

    public void Bind(IWorkspaceService ws)
    {
        _workspace = ws;
        _workspace.ActiveImageChanged += (s, e) => Refresh(e.CurrentPath);
    }

    /// <summary>Bind kèm meta + settings để bật trình sửa Keywords (D6.4).</summary>
    public void Bind(IWorkspaceService ws, IImageMetaService meta, ISettingsService settings)
    {
        _meta = meta;
        _settings = settings;
        _meta.MetaChanged += (s, e) =>
        {
            if (string.Equals(e.ImagePath, _currentPath, System.StringComparison.OrdinalIgnoreCase))
                Dispatcher.BeginInvoke(() => LoadKeywords(e.ImagePath));
        };
        Bind(ws);
    }

    private void Refresh(string? path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _currentPath = path;

        Dispatcher.BeginInvoke(() =>
        {
            Exif.Clear();
            Colors.Clear();
            Suggestions.Clear();
            imgHistogram.Source = null;
            txtHistEmpty.Visibility = Visibility.Visible;
            txtCaptureSummary.Visibility = Visibility.Collapsed;
            txtContrastAdvice.Visibility = Visibility.Collapsed;
            btnSaveMeta.IsEnabled = false;
            ClearMetaFields();
            LoadKeywords(path);
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
                double gpsLat = 0, gpsLon = 0;
                bool hasGps = false;
                rows.Add(new ExifRow("File", fi.Name));
                rows.Add(new ExifRow("Size", $"{fi.Length / 1024.0:N0} KB"));
                rows.Add(new ExifRow("Pixels", $"{img.Width} x {img.Height}"));
                rows.Add(new ExifRow("Modified", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")));

                if (img.Metadata.ExifProfile != null)
                {
                    // GPS (8.5): hiển thị toạ độ + lưu để mở bản đồ.
                    if (ImageTool.Shared.ExifReader.TryReadGps(img.Metadata.ExifProfile, out var gLat, out var gLon))
                    {
                        rows.Insert(0, new ExifRow("GPS", ImageTool.Shared.GpsHelper.Format(gLat, gLon)));
                        gpsLat = gLat; gpsLon = gLon; hasGps = true;
                    }

                    foreach (var v in img.Metadata.ExifProfile.Values)
                    {
                        if (ct.IsCancellationRequested) return;
                        var val = v.GetValue()?.ToString();
                        if (string.IsNullOrEmpty(val)) continue;
                        if (val.Length > 80) val = val.Substring(0, 80) + "…";
                        rows.Add(new ExifRow(v.Tag.ToString() ?? "", val));
                    }
                }

                // Dòng tóm tắt thông số chụp từ catalog metadata (camera/lens/exposure).
                string summary = BuildCaptureSummary(path);

                // Bảng màu chủ đạo (K-Means trên ảnh đã load).
                var swatches = ImageTool.Shared.DominantColors.Extract(img, k: 6);
                if (ct.IsCancellationRequested) return;

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
                    _gpsLat = gpsLat; _gpsLon = gpsLon; _hasGps = hasGps;
                    btnMap.Visibility = hasGps ? Visibility.Visible : Visibility.Collapsed;

                    // Tóm tắt chụp.
                    if (!string.IsNullOrEmpty(summary))
                    {
                        txtCaptureSummary.Text = summary;
                        txtCaptureSummary.Visibility = Visibility.Visible;
                    }

                    // Bảng màu.
                    Colors.Clear();
                    foreach (var s in swatches)
                        Colors.Add(new ColorSwatchVm(s.Hex, s.PercentText,
                            new SolidColorBrush(System.Windows.Media.Color.FromRgb(s.R, s.G, s.B))));

                    // Gợi ý màu (color theory) từ màu chủ đạo + đánh giá tương phản.
                    Suggestions.Clear();
                    if (swatches.Count > 0)
                    {
                        var top = swatches[0];
                        foreach (var sg in ImageTool.Shared.ColorSuggestion.FromDominant(top.R, top.G, top.B))
                            Suggestions.Add(new ColorSwatchVm(sg.Hex, sg.Role,
                                new SolidColorBrush(System.Windows.Media.Color.FromRgb(sg.R, sg.G, sg.B))));

                        var swList = swatches.Select(s => (s.R, s.G, s.B)).ToList();
                        var (score, advice) = ImageTool.Shared.ColorSuggestion.AssessContrast(swList);
                        txtContrastAdvice.Text = $"Tương phản màu: {score * 100:0}% — {advice}";
                        txtContrastAdvice.Visibility = Visibility.Visible;
                    }

                    // Form sửa metadata.
                    LoadMetaFields(path);
                    btnSaveMeta.IsEnabled = true;
                    LoadKeywords(path);
                });
            }
            catch (Exception ex) { ImageTool.Shared.AppLog.Error("InfoPanel.Refresh", path, ex); }
        }, ct);
    }

    private double _gpsLat, _gpsLon;
    private bool _hasGps;

    private void BtnMap_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasGps) return;
        try
        {
            var url = ImageTool.Shared.GpsHelper.GoogleMapsUrl(_gpsLat, _gpsLon);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Warn("InfoPanel.Map", ex.Message); }
    }

    /// <summary>Dòng tóm tắt chụp từ EXIF: "Canon R5 · 50mm · f/1.8 · 1/200s · ISO 400".</summary>
    private static string BuildCaptureSummary(string path)
    {
        try
        {
            var ci = ImageTool.Shared.ExifReader.ReadMetadata(path);
            var parts = new List<string>();
            var camera = string.Join(" ", new[] { ci.CameraMake, ci.CameraModel }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (!string.IsNullOrWhiteSpace(camera)) parts.Add(camera);
            if (ci.FocalLength is > 0) parts.Add($"{ci.FocalLength:0.#}mm");
            if (ci.Aperture is > 0) parts.Add($"f/{ci.Aperture:0.#}");
            if (!string.IsNullOrWhiteSpace(ci.ShutterSpeed)) parts.Add(ci.ShutterSpeed!);
            if (ci.Iso is > 0) parts.Add($"ISO {ci.Iso}");
            return string.Join("  ·  ", parts);
        }
        catch { return ""; }
    }

    private void ColorSwatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string hex)
        {
            try { System.Windows.Clipboard.SetText(hex); }
            catch (Exception ex) { ImageTool.Shared.AppLog.Warn("InfoPanel.CopyHex", ex.Message); }
        }
    }

    // ===== Sửa metadata (gộp từ MetaEditor) =====
    private void ClearMetaFields()
    {
        txtDescription.Text = ""; txtArtist.Text = ""; txtCopyright.Text = "";
        txtSoftware.Text = ""; txtMake.Text = ""; txtModel.Text = "";
    }

    private void LoadMetaFields(string path)
    {
        try
        {
            var m = ImageTool.Shared.ExifWriter.ReadEditable(path);
            txtDescription.Text = m.GetValueOrDefault("ImageDescription", "");
            txtArtist.Text = m.GetValueOrDefault("Artist", "");
            txtCopyright.Text = m.GetValueOrDefault("Copyright", "");
            txtSoftware.Text = m.GetValueOrDefault("Software", "");
            txtMake.Text = m.GetValueOrDefault("Make", "");
            txtModel.Text = m.GetValueOrDefault("Model", "");
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Warn("InfoPanel.LoadMeta", ex.Message); }
    }

    private void BtnSaveMeta_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentPath) || !File.Exists(_currentPath)) return;
        var values = new Dictionary<string, string>
        {
            ["ImageDescription"] = txtDescription.Text,
            ["Artist"] = txtArtist.Text,
            ["Copyright"] = txtCopyright.Text,
            ["Software"] = txtSoftware.Text,
            ["Make"] = txtMake.Text,
            ["Model"] = txtModel.Text,
        };
        bool ok = ImageTool.Shared.ExifWriter.Write(_currentPath, values);
        MessageBox.Show(ok ? "Đã lưu metadata vào ảnh." : "Không lưu được metadata (xem app.log).",
            "Metadata", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        if (ok) Refresh(_currentPath);
    }

    // ===== Keywords / Tags (D6.4) =====

    /// <summary>VM 1 keyword: hiện đoạn lá nhưng giữ full-path để gỡ/thêm chính xác.</summary>
    public sealed class KeywordVm
    {
        public string Full { get; init; } = "";
        public string Leaf { get; init; } = "";
        public static KeywordVm From(string full)
            => new() { Full = full, Leaf = ImageTool.Shared.KeywordHelper.LeafName(full) };
    }

    private void LoadKeywords(string? path)
    {
        Keywords.Clear();
        KeywordSuggestions.Clear();
        if (_meta == null || string.IsNullOrEmpty(path)) { UpdateKeywordEmpty(); return; }

        var current = ImageTool.Shared.KeywordHelper.NormalizeList(_meta.Get(path).Tags);
        foreach (var k in current) Keywords.Add(KeywordVm.From(k));
        UpdateKeywordEmpty();

        // Gợi ý = (recent + từ điển) trừ tag đã gắn, tối đa 12 mục.
        if (_settings != null)
        {
            var has = new System.Collections.Generic.HashSet<string>(current, System.StringComparer.OrdinalIgnoreCase);
            var seen = new System.Collections.Generic.HashSet<string>(has, System.StringComparer.OrdinalIgnoreCase);
            var pool = new System.Collections.Generic.List<string>();
            pool.AddRange(_settings.Current.RecentTags);
            pool.AddRange(_settings.Current.TagDictionary);
            foreach (var t in pool)
            {
                var n = ImageTool.Shared.KeywordHelper.Normalize(t);
                if (n == null || !seen.Add(n)) continue;
                KeywordSuggestions.Add(KeywordVm.From(n));
                if (KeywordSuggestions.Count >= 12) break;
            }
        }
    }

    private void UpdateKeywordEmpty()
    {
        txtNoKeywords.Visibility = Keywords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KeywordInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { AddKeywordFromInput(); e.Handled = true; }
    }

    private void AddKeyword_Click(object sender, RoutedEventArgs e) => AddKeywordFromInput();

    private void AddKeywordFromInput()
    {
        var n = ImageTool.Shared.KeywordHelper.Normalize(txtKeywordInput.Text);
        txtKeywordInput.Text = "";
        if (n != null) ApplyAddKeyword(n);
    }

    private void SuggestKeyword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string full)
            ApplyAddKeyword(full);
    }

    private void ApplyAddKeyword(string keyword)
    {
        if (_meta == null || string.IsNullOrEmpty(_currentPath)) return;
        var n = ImageTool.Shared.KeywordHelper.Normalize(keyword);
        if (n == null) return;
        var list = ImageTool.Shared.KeywordHelper.NormalizeList(_meta.Get(_currentPath).Tags);
        if (list.Any(x => string.Equals(x, n, System.StringComparison.OrdinalIgnoreCase))) return;
        list.Add(n);
        _meta.SetTags(_currentPath, list);
        _settings?.AddRecentTags(new[] { n });
        LoadKeywords(_currentPath);
    }

    private void RemoveKeyword_Click(object sender, RoutedEventArgs e)
    {
        if (_meta == null || string.IsNullOrEmpty(_currentPath)) return;
        if (sender is not FrameworkElement fe || fe.Tag is not string full) return;
        var list = ImageTool.Shared.KeywordHelper.NormalizeList(_meta.Get(_currentPath).Tags);
        list.RemoveAll(x => string.Equals(x, full, System.StringComparison.OrdinalIgnoreCase));
        _meta.SetTags(_currentPath, list);
        LoadKeywords(_currentPath);
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

/// <summary>1 ô màu chủ đạo cho ItemsControl (gộp từ ColorLab). Brush dùng để vẽ swatch.</summary>
/// <summary>1 ô màu chủ đạo cho ItemsControl (gộp từ ColorLab). Brush dùng để vẽ swatch.
/// PercentText dùng cho palette; Role là alias cùng giá trị cho ô gợi ý màu (color theory).</summary>
public record ColorSwatchVm(string Hex, string PercentText, Brush Brush)
{
    public string Role => PercentText;
}
