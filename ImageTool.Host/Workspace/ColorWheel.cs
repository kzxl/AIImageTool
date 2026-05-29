using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ImageTool.Host.Workspace;

/// <summary>
/// Color-wheel kiểu Lightroom Color Grading: 1 vòng tròn hue (góc) + bão hoà (bán kính).
/// Kéo "ngón tay" (thumb) quanh vòng để chọn Hue (0..360) + Sat (0..1). Phát
/// <see cref="ColorChanged"/> với (hue, sat). Nhẹ, vẽ bằng ConicGradient giả lập qua nhiều nêm.
/// </summary>
public sealed class ColorWheel : Canvas
{
    private Ellipse? _ring;
    private Ellipse? _thumb;
    private double _radius;
    private Point _center;
    private bool _dragging;
    private bool _suppress;

    public float Hue { get; private set; }
    public float Sat { get; private set; }

    /// <summary>Bắn (hue 0..360, sat 0..1) khi user kéo.</summary>
    public event EventHandler<(float hue, float sat)>? ColorChanged;

    public ColorWheel()
    {
        Width = 120; Height = 120;
        Background = Brushes.Transparent;
        SizeChanged += (_, _) => Redraw();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        Loaded += (_, _) => Redraw();
    }

    public void SetValue(float hue, float sat)
    {
        _suppress = true;
        Hue = ((hue % 360f) + 360f) % 360f;
        Sat = Math.Clamp(sat, 0f, 1f);
        _suppress = false;
        UpdateThumb();
    }

    private void Redraw()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        Children.Clear();
        _radius = Math.Min(ActualWidth, ActualHeight) / 2 - 8;
        _center = new Point(ActualWidth / 2, ActualHeight / 2);

        // vẽ vòng hue bằng nhiều nêm (wedge) màu bão hoà đầy.
        const int wedges = 72;
        for (int i = 0; i < wedges; i++)
        {
            double a0 = i / (double)wedges * 2 * Math.PI;
            double a1 = (i + 1) / (double)wedges * 2 * Math.PI;
            float hue = (float)(i / (double)wedges * 360.0);
            var col = HsvToColor(hue, 1f, 1f);
            var fig = new PathFigure { StartPoint = _center, IsClosed = true };
            fig.Segments.Add(new LineSegment(PointOnCircle(a0, _radius), true));
            fig.Segments.Add(new ArcSegment(PointOnCircle(a1, _radius),
                new Size(_radius, _radius), 0, false, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            Children.Add(new Path { Fill = new SolidColorBrush(col), Data = geo });
        }

        // overlay radial trắng -> trong suốt để thể hiện sat (tâm trắng = sat thấp).
        var inner = new Ellipse
        {
            Width = _radius * 2, Height = _radius * 2,
            Fill = new RadialGradientBrush(Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
            { RadiusX = 0.5, RadiusY = 0.5 }
        };
        SetLeft(inner, _center.X - _radius);
        SetTop(inner, _center.Y - _radius);
        Children.Add(inner);

        _ring = new Ellipse
        {
            Width = _radius * 2, Height = _radius * 2,
            Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), StrokeThickness = 1
        };
        SetLeft(_ring, _center.X - _radius);
        SetTop(_ring, _center.Y - _radius);
        Children.Add(_ring);

        _thumb = new Ellipse
        {
            Width = 12, Height = 12,
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.5
        };
        Children.Add(_thumb);
        UpdateThumb();
    }

    private void UpdateThumb()
    {
        if (_thumb == null) return;
        double ang = Hue * Math.PI / 180.0;
        double r = Sat * _radius;
        var p = new Point(_center.X + Math.Cos(ang) * r, _center.Y - Math.Sin(ang) * r);
        SetLeft(_thumb, p.X - 6);
        SetTop(_thumb, p.Y - 6);
    }

    private Point PointOnCircle(double angle, double r)
        => new(_center.X + Math.Cos(angle) * r, _center.Y - Math.Sin(angle) * r);

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        UpdateFromPoint(e.GetPosition(this));
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragging) UpdateFromPoint(e.GetPosition(this));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) { _dragging = false; ReleaseMouseCapture(); }
    }

    private void UpdateFromPoint(Point p)
    {
        double dx = p.X - _center.X, dy = _center.Y - p.Y;
        double ang = Math.Atan2(dy, dx);
        if (ang < 0) ang += 2 * Math.PI;
        double r = Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy) / Math.Max(1, _radius));
        Hue = (float)(ang * 180.0 / Math.PI);
        Sat = (float)r;
        UpdateThumb();
        if (!_suppress) ColorChanged?.Invoke(this, (Hue, Sat));
    }

    private static Color HsvToColor(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }
        return Color.FromRgb(
            (byte)Math.Clamp((r1 + m) * 255, 0, 255),
            (byte)Math.Clamp((g1 + m) * 255, 0, 255),
            (byte)Math.Clamp((b1 + m) * 255, 0, 255));
    }
}
