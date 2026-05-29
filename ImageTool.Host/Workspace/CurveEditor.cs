using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

/// <summary>
/// Tone-curve editor tương tác: vẽ đường cong monotone-cubic (qua <see cref="CurveMath"/> nên
/// khớp 100% với ToneCurveOp khi áp), cho kéo điểm điều khiển, double-click thêm điểm, chuột
/// phải xoá điểm. Phát sự kiện <see cref="CurveChanged"/> khi user chỉnh (để debounce + commit).
///
/// Toạ độ điểm ở [0..1]×[0..1] (y hướng lên). Canvas vẽ với y đảo (0 ở đáy).
/// </summary>
public sealed class CurveEditor : Canvas
{
    private readonly List<(float x, float y)> _points = new() { (0f, 0f), (1f, 1f) };
    private readonly List<Ellipse> _thumbs = new();
    private Path? _curvePath;
    private int _dragIndex = -1;
    private const double ThumbSize = 10;
    private const double EdgeHitMargin = 0.04; // bán kính click chọn điểm (toạ độ chuẩn hoá)

    /// <summary>Bắn khi user thay đổi điểm (kéo/thêm/xoá). Đối số = serialize "x,y;..." của curve.</summary>
    public event EventHandler<string>? CurveChanged;

    /// <summary>True khi đang nạp điểm từ ngoài (không bắn sự kiện).</summary>
    private bool _suppress;

    public CurveEditor()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Height = 180;
        ClipToBounds = true;
        MinWidth = 120;
        SizeChanged += (_, _) => Redraw();
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonDown += OnRightDown;
        Loaded += (_, _) => Redraw();
    }

    /// <summary>Nạp điểm từ chuỗi serialize (vd từ history). Không bắn sự kiện.</summary>
    public void SetPoints(string? serialized)
    {
        var pts = CurveMath.Parse(serialized) ?? new List<(float, float)> { (0f, 0f), (1f, 1f) };
        _suppress = true;
        _points.Clear();
        _points.AddRange(CurveMath.Normalize(pts));
        _suppress = false;
        Redraw();
    }

    /// <summary>Trả serialize hiện tại.</summary>
    public string GetSerialized() => CurveMath.Serialize(_points);

    public bool IsIdentity => CurveMath.IsIdentity(CurveMath.Normalize(_points));

    /// <summary>Đưa về đường chéo identity và bắn sự kiện.</summary>
    public void Reset()
    {
        _points.Clear();
        _points.Add((0f, 0f));
        _points.Add((1f, 1f));
        Redraw();
        RaiseChanged();
    }

    // ----- toạ độ: chuẩn hoá <-> pixel -----
    private Point ToPixel(float nx, float ny)
        => new(nx * ActualWidth, (1f - ny) * ActualHeight);

    private (float x, float y) ToNorm(Point p)
        => ((float)Math.Clamp(p.X / Math.Max(1, ActualWidth), 0, 1),
            (float)Math.Clamp(1 - p.Y / Math.Max(1, ActualHeight), 0, 1));

    private void Redraw()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        Children.Clear();
        _thumbs.Clear();

        DrawGrid();

        // đường cong từ LUT (khớp op).
        var lut = CurveMath.BuildLut(_points);
        var fig = new PathFigure { StartPoint = new Point(0, (1 - lut[0]) * ActualHeight) };
        int n = CurveMath.LutSize;
        for (int i = 1; i < n; i++)
        {
            double x = i / (double)(n - 1) * ActualWidth;
            double y = (1 - lut[i]) * ActualHeight;
            fig.Segments.Add(new LineSegment(new Point(x, y), true));
        }
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        _curvePath = new Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            StrokeThickness = 1.6,
            Data = geo
        };
        Children.Add(_curvePath);

        // điểm điều khiển.
        for (int i = 0; i < _points.Count; i++)
        {
            var px = ToPixel(_points[i].x, _points[i].y);
            var e = new Ellipse
            {
                Width = ThumbSize, Height = ThumbSize,
                Fill = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
                Stroke = Brushes.White, StrokeThickness = 1, Tag = i
            };
            SetLeft(e, px.X - ThumbSize / 2);
            SetTop(e, px.Y - ThumbSize / 2);
            Children.Add(e);
            _thumbs.Add(e);
        }
    }

    private void DrawGrid()
    {
        var grid = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        for (int i = 1; i < 4; i++)
        {
            double fx = i / 4.0 * ActualWidth;
            double fy = i / 4.0 * ActualHeight;
            Children.Add(new Line { X1 = fx, Y1 = 0, X2 = fx, Y2 = ActualHeight, Stroke = grid, StrokeThickness = 0.5 });
            Children.Add(new Line { X1 = 0, Y1 = fy, X2 = ActualWidth, Y2 = fy, Stroke = grid, StrokeThickness = 0.5 });
        }
        // đường chéo tham chiếu.
        Children.Add(new Line
        {
            X1 = 0, Y1 = ActualHeight, X2 = ActualWidth, Y2 = 0,
            Stroke = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)), StrokeThickness = 0.5
        });
    }

    private int HitTest(Point p)
    {
        var np = ToNorm(p);
        int best = -1; double bestD = EdgeHitMargin * EdgeHitMargin;
        for (int i = 0; i < _points.Count; i++)
        {
            double dx = _points[i].x - np.x, dy = _points[i].y - np.y;
            double d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        int hit = HitTest(p);
        if (e.ClickCount == 2)
        {
            if (hit >= 0 && hit != 0 && hit != _points.Count - 1)
            {
                // double-click trên điểm giữa -> xoá.
                _points.RemoveAt(hit);
                Redraw();
                RaiseChanged();
            }
            else if (hit < 0)
            {
                // double-click chỗ trống -> thêm điểm.
                var np = ToNorm(p);
                _points.Add(np);
                _points.Sort((a, b) => a.x.CompareTo(b.x));
                Redraw();
                RaiseChanged();
            }
            return;
        }
        if (hit >= 0)
        {
            _dragIndex = hit;
            CaptureMouse();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragIndex < 0) return;
        var np = ToNorm(e.GetPosition(this));
        // điểm đầu/cuối khoá x (chỉ chỉnh y); điểm giữa kẹp giữa 2 hàng xóm.
        float x;
        bool isFirst = _dragIndex == 0, isLast = _dragIndex == _points.Count - 1;
        if (isFirst) x = 0f;
        else if (isLast) x = 1f;
        else
        {
            float lo = _points[_dragIndex - 1].x + 0.001f;
            float hi = _points[_dragIndex + 1].x - 0.001f;
            x = Math.Clamp(np.x, lo, hi);
        }
        _points[_dragIndex] = (x, np.y);
        Redraw();
        RaiseChanged();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            ReleaseMouseCapture();
        }
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        int hit = HitTest(e.GetPosition(this));
        if (hit > 0 && hit < _points.Count - 1)
        {
            _points.RemoveAt(hit);
            Redraw();
            RaiseChanged();
            e.Handled = true;
        }
    }

    private void RaiseChanged()
    {
        if (_suppress) return;
        CurveChanged?.Invoke(this, GetSerialized());
    }
}
