using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

// Histogram trực quan + cảnh báo clip trong DevelopPanel (11.3).
public partial class DevelopPanel
{
    private Border? _histHost;
    private Canvas? _histCanvas;
    private TextBlock? _histClipLabel;
    // Chế độ hiển thị kênh: 0 = RGB chồng, 1 = Luma.
    private int _histChannelMode;
    // Chế độ scope: 0 = histogram, 1 = waveform/parade.
    private int _scopeMode;
    private ToggleButton? _histBtnRgb;
    private ToggleButton? _histBtnLuma;
    private ToggleButton? _histBtnWave;

    /// <summary>Dựng widget histogram (gọi đầu BuildUI, ghim trên cùng panel slider).</summary>
    private FrameworkElement BuildHistogram()
    {
        var outer = new StackPanel { Margin = new Thickness(2, 2, 2, 6) };

        // Hàng nút chọn kênh RGB / Luma (13.8) + scope Wave.
        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 2) };
        _histBtnRgb = new ToggleButton { Content = "RGB", FontSize = 10, Padding = new Thickness(6, 1, 6, 1), IsChecked = true, Margin = new Thickness(0, 0, 4, 0) };
        _histBtnLuma = new ToggleButton { Content = "Luma", FontSize = 10, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 8, 0) };
        _histBtnWave = new ToggleButton { Content = "Wave", FontSize = 10, Padding = new Thickness(6, 1, 6, 1), ToolTip = "Bật waveform / RGB-parade (phân bố theo cột ảnh)" };
        _histBtnRgb.Click += (_, _) => SetHistChannelMode(0);
        _histBtnLuma.Click += (_, _) => SetHistChannelMode(1);
        _histBtnWave.Click += (_, _) => SetScopeMode(_histBtnWave.IsChecked == true ? 1 : 0);
        toggleRow.Children.Add(_histBtnRgb);
        toggleRow.Children.Add(_histBtnLuma);
        toggleRow.Children.Add(_histBtnWave);
        outer.Children.Add(toggleRow);

        _histCanvas = new Canvas
        {
            Height = 110,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            ClipToBounds = true,
            Cursor = System.Windows.Input.Cursors.SizeWE,
            ToolTip = "Kéo ngang trên histogram để chỉnh tone: trái→phải = Blacks · Shadows · Exposure · Highlights · Whites"
        };
        _histCanvas.SizeChanged += (_, _) => DrawScope();
        _histCanvas.MouseLeftButtonDown += HistCanvas_MouseDown;
        _histCanvas.MouseMove += HistCanvas_MouseMove;
        _histCanvas.MouseLeftButtonUp += HistCanvas_MouseUp;
        _histHost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            Child = _histCanvas
        };
        outer.Children.Add(_histHost);
        _histClipLabel = new TextBlock { Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(2, 2, 0, 0) };
        outer.Children.Add(_histClipLabel);
        return outer;
    }

    private void SetHistChannelMode(int mode)
    {
        _histChannelMode = mode;
        if (_histBtnRgb != null) _histBtnRgb.IsChecked = mode == 0;
        if (_histBtnLuma != null) _histBtnLuma.IsChecked = mode == 1;
        DrawScope();
    }

    private void SetScopeMode(int mode)
    {
        _scopeMode = mode;
        if (_histBtnWave != null) _histBtnWave.IsChecked = mode == 1;
        RefreshHistogram(); // tải lại dữ liệu phù hợp (waveform/histogram)
    }

    private HistogramData? _lastHist;
    private WaveformData? _lastWave;

    /// <summary>Tính lại scope cho ảnh + ops hiện tại rồi vẽ. Gọi off-UI để khỏi giật.</summary>
    private void RefreshHistogram()
    {
        if (_renderer == null || _history == null || string.IsNullOrEmpty(_currentPath) || _histCanvas == null) return;
        var path = _currentPath;
        var ops = _history.GetStack(path);
        int ptr = _history.GetPointer(path);
        bool wave = _scopeMode == 1;
        System.Threading.Tasks.Task.Run(() =>
        {
            if (wave)
            {
                var wf = _renderer.ComputeWaveform(path, ops, ptr, 256);
                if (wf == null) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (!string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase)) return;
                    _lastWave = wf;
                    DrawScope();
                });
            }
            else
            {
                var hist = _renderer.ComputeHistogram(path, ops, ptr);
                if (hist == null) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (!string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase)) return;
                    _lastHist = hist;
                    DrawScope();
                });
            }
        });
    }

    private void DrawScope()
    {
        if (_scopeMode == 1) DrawWaveform();
        else DrawHistogram();
    }

    private void DrawHistogram()
    {
        if (_histCanvas == null || _lastHist == null) return;
        double w = _histCanvas.ActualWidth, h = _histCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _histCanvas.Children.Clear();

        var hist = _lastHist;
        if (_histChannelMode == 1)
        {
            // Luma: 1 đường xám.
            int lmax = 1;
            for (int i = 0; i < 256; i++) if (hist.Luma[i] > lmax) lmax = hist.Luma[i];
            DrawChannelPath(hist.Luma, lmax, w, h, Color.FromArgb(160, 220, 225, 230));
        }
        else
        {
            int max = hist.MaxBin();
            DrawChannelPath(hist.R, max, w, h, Color.FromArgb(120, 255, 77, 109)); // Neon Pink/Red
            DrawChannelPath(hist.G, max, w, h, Color.FromArgb(120, 6, 214, 160));  // Neon Mint/Green
            DrawChannelPath(hist.B, max, w, h, Color.FromArgb(120, 58, 134, 255)); // Neon Blue
        }

        // marker clip: tam giác góc trên trái (shadow) / phải (highlight).
        if (hist.ShadowClipWarning)
            _histCanvas.Children.Add(ClipTriangle(0, 0, 9, Color.FromRgb(0x50, 0xA0, 0xFF)));
        if (hist.HighlightClipWarning)
            _histCanvas.Children.Add(ClipTriangle(w - 9, 0, 9, Color.FromRgb(0xFF, 0x5A, 0x5A)));

        if (_histClipLabel != null)
        {
            var parts = new List<string>();
            if (hist.ShadowClipWarning) parts.Add($"▼ tối {hist.ShadowClipPercent:0.0}%");
            if (hist.HighlightClipWarning) parts.Add($"▲ sáng {hist.HighlightClipPercent:0.0}%");
            _histClipLabel.Text = parts.Count > 0 ? string.Join("   ", parts) : "Không clip";
            _histClipLabel.Foreground = parts.Count > 0 ? Brushes.Orange : Brushes.Gray;
        }
    }

    private void DrawChannelPath(int[] data, int max, double w, double h, Color color)
    {
        if (_histCanvas == null) return;
        var fig = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
        for (int i = 0; i < 256; i++)
        {
            double x = i / 255.0 * w;
            double y = h - (double)data[i] / max * h;
            fig.Segments.Add(new LineSegment(new Point(x, y), true));
        }
        fig.Segments.Add(new LineSegment(new Point(w, h), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        _histCanvas.Children.Add(new Path { Fill = new SolidColorBrush(color), Data = geo });
    }

    private static Polygon ClipTriangle(double x, double y, double s, Color c) => new()
    {
        Fill = new SolidColorBrush(c),
        Points = new PointCollection { new(x, y), new(x + s, y), new(x, y + s) }
    };

    /// <summary>Vẽ waveform/RGB-parade bằng WriteableBitmap (additive theo cột). RGB mode = 3 kênh chồng màu.</summary>
    private void DrawWaveform()
    {
        if (_histCanvas == null || _lastWave == null) return;
        double cw = _histCanvas.ActualWidth, ch = _histCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;
        _histCanvas.Children.Clear();

        var wf = _lastWave;
        int cols = wf.Columns;
        // Bitmap nội bộ: rộng = số cột, cao = 256 (mức), vẽ rồi để Image stretch ra canvas.
        int bw = cols, bh = 256;
        var bmp = new System.Windows.Media.Imaging.WriteableBitmap(bw, bh, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var buf = new byte[bw * bh * 4];

        // Chuẩn hoá theo log để vệt mờ không bị chìm. gain ~ độ sáng vệt.
        float norm = wf.MaxCount > 0 ? 1f / MathF.Log(1 + wf.MaxCount) : 1f;
        bool luma = _histChannelMode == 1;

        for (int c = 0; c < cols; c++)
        {
            for (int v = 0; v < 256; v++)
            {
                // y đảo: mức 255 (sáng) ở trên cùng.
                int yRow = (255 - v) * bw;
                int o = (yRow + c) * 4;
                if (luma)
                {
                    byte i = Intensity(wf.Luma[c, v], norm);
                    if (i == 0) continue;
                    buf[o] = i; buf[o + 1] = i; buf[o + 2] = i; buf[o + 3] = 255;
                }
                else
                {
                    byte rr = Intensity(wf.R[c, v], norm);
                    byte gg = Intensity(wf.G[c, v], norm);
                    byte bb = Intensity(wf.B[c, v], norm);
                    if ((rr | gg | bb) == 0) continue;
                    buf[o] = bb; buf[o + 1] = gg; buf[o + 2] = rr; buf[o + 3] = 255; // BGRA
                }
            }
        }
        bmp.WritePixels(new Int32Rect(0, 0, bw, bh), buf, bw * 4, 0);

        var img = new System.Windows.Controls.Image
        {
            Source = bmp, Stretch = Stretch.Fill, Width = cw, Height = ch,
            // pixel scope nét hơn khi không làm mượt quá.
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.Linear);
        _histCanvas.Children.Add(img);

        if (_histClipLabel != null)
        {
            _histClipLabel.Text = luma ? "Waveform (Luma)" : "RGB Parade";
            _histClipLabel.Foreground = Brushes.Gray;
        }
    }

    private static byte Intensity(int count, float norm)
    {
        if (count <= 0) return 0;
        float t = MathF.Log(1 + count) * norm; // [0..1]
        int v = (int)(40 + t * 215); // sàn 40 để vệt mảnh vẫn thấy
        if (v > 255) v = 255;
        return (byte)v;
    }

    // ===== Kéo trực tiếp trên histogram để chỉnh tone (13.10) =====
    // Chia trục ngang [0..1] làm 5 vùng tone, mỗi vùng map sang 1 slider Basic. Kéo ngang:
    // sang phải = tăng, sang trái = giảm. Bước nhỏ để mượt; commit debounce như slider thường.
    private bool _histDragging;
    private double _histDragLastX;
    private string? _histDragKey;

    private void HistCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_histCanvas == null || _loading || string.IsNullOrEmpty(_currentPath)) return;
        if (_scopeMode == 1) return; // waveform: không kéo chỉnh tone
        double w = _histCanvas.ActualWidth;
        if (w <= 0) return;
        double x = e.GetPosition(_histCanvas).X;
        _histDragKey = ToneKeyAt(x / w);
        _histDragging = true;
        _histDragLastX = x;
        _histCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void HistCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_histDragging || _histCanvas == null || _histDragKey == null) return;
        double w = _histCanvas.ActualWidth;
        if (w <= 0) return;
        double x = e.GetPosition(_histCanvas).X;
        double dxFrac = (x - _histDragLastX) / w;   // tỉ lệ chiều ngang đã kéo
        _histDragLastX = x;
        if (Math.Abs(dxFrac) < 1e-4) return;

        // Exposure thang [-5..5] nhạy hơn; còn lại [-1..1].
        double gain = _histDragKey == "exposure" ? 4.0 : 1.6;
        double cur = GetVal(_histDragKey);
        double next = cur + dxFrac * gain;
        next = _histDragKey == "exposure" ? Math.Clamp(next, -5, 5) : Math.Clamp(next, -1, 1);
        SetVal(_histDragKey, next);   // slider.ValueChanged tự ScheduleCommit (debounce)
    }

    private void HistCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_histDragging) return;
        _histDragging = false;
        _histDragKey = null;
        _histCanvas?.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Vùng tone theo vị trí ngang chuẩn hoá [0..1] -> slider Basic tương ứng.</summary>
    private static string ToneKeyAt(double xFrac) => xFrac switch
    {
        < 0.20 => "blacks",
        < 0.40 => "shadows",
        < 0.60 => "exposure",
        < 0.80 => "highlights",
        _ => "whites",
    };
}
