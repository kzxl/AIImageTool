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
    private ToggleButton? _histBtnRgb;
    private ToggleButton? _histBtnLuma;

    /// <summary>Dựng widget histogram (gọi đầu BuildUI, ghim trên cùng panel slider).</summary>
    private FrameworkElement BuildHistogram()
    {
        var outer = new StackPanel { Margin = new Thickness(2, 2, 2, 6) };

        // Hàng nút chọn kênh RGB / Luma (13.8).
        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 2) };
        _histBtnRgb = new ToggleButton { Content = "RGB", FontSize = 10, Padding = new Thickness(6, 1, 6, 1), IsChecked = true, Margin = new Thickness(0, 0, 4, 0) };
        _histBtnLuma = new ToggleButton { Content = "Luma", FontSize = 10, Padding = new Thickness(6, 1, 6, 1) };
        _histBtnRgb.Click += (_, _) => SetHistChannelMode(0);
        _histBtnLuma.Click += (_, _) => SetHistChannelMode(1);
        toggleRow.Children.Add(_histBtnRgb);
        toggleRow.Children.Add(_histBtnLuma);
        outer.Children.Add(toggleRow);

        _histCanvas = new Canvas
        {
            Height = 90,
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)),
            ClipToBounds = true
        };
        _histCanvas.SizeChanged += (_, _) => DrawHistogram();
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
        DrawHistogram();
    }

    private HistogramData? _lastHist;

    /// <summary>Tính lại histogram cho ảnh + ops hiện tại rồi vẽ. Gọi off-UI để khỏi giật.</summary>
    private void RefreshHistogram()
    {
        if (_renderer == null || _history == null || string.IsNullOrEmpty(_currentPath) || _histCanvas == null) return;
        var path = _currentPath;
        var ops = _history.GetStack(path);
        int ptr = _history.GetPointer(path);
        System.Threading.Tasks.Task.Run(() =>
        {
            var hist = _renderer.ComputeHistogram(path, ops, ptr);
            if (hist == null) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase)) return;
                _lastHist = hist;
                DrawHistogram();
            });
        });
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
            DrawChannelPath(hist.Luma, lmax, w, h, Color.FromArgb(190, 0xCC, 0xCC, 0xCC));
        }
        else
        {
            int max = hist.MaxBin();
            DrawChannelPath(hist.R, max, w, h, Color.FromArgb(150, 0xE0, 0x50, 0x50));
            DrawChannelPath(hist.G, max, w, h, Color.FromArgb(150, 0x50, 0xD0, 0x50));
            DrawChannelPath(hist.B, max, w, h, Color.FromArgb(150, 0x50, 0x90, 0xF0));
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
}
