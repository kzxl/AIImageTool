using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ImageTool.Host.Workspace;

// Brush stroke capture cho local brush mask (6.4).
public partial class CenterPreview
{
    private LocalMask? _brushMask;
    private bool _brushing;

    /// <summary>Liên kết DevelopPanel để nhận tín hiệu brush mask được chọn (bật/tắt lớp vẽ).</summary>
    public void BindBrushPanel(DevelopPanel panel)
    {
        panel.BrushMaskActivated += (s, mask) =>
        {
            _brushMask = mask;
            brushOverlay.Visibility = mask != null ? Visibility.Visible : Visibility.Collapsed;
            if (mask != null) { SetMode(LighttableMode.Single); ResetZoom(); }
            brushOverlay.Children.Clear();
        };
    }

    private void BrushOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_brushMask == null) return;
        _brushing = true;
        brushOverlay.CaptureMouse();
        AddBrushPointFrom(e.GetPosition(brushOverlay));
        e.Handled = true;
    }

    private void BrushOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_brushing || _brushMask == null) return;
        // Polygon/Path: chỉ thêm đỉnh khi click (không thêm liên tục khi kéo).
        if (_brushMask.MaskType == ImageTool.Imaging.PolygonMask.Type ||
            _brushMask.MaskType == ImageTool.Imaging.PathMask.Type) return;
        AddBrushPointFrom(e.GetPosition(brushOverlay));
    }

    private void BrushOverlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_brushing) return;
        _brushing = false;
        brushOverlay.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void AddBrushPointFrom(Point p)
    {
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0 || img.Height <= 0) return;
        // chỉ nhận điểm trong vùng ảnh.
        if (p.X < img.Left || p.X > img.Right || p.Y < img.Top || p.Y > img.Bottom) return;
        float nx = (float)((p.X - img.Left) / img.Width);
        float ny = (float)((p.Y - img.Top) / img.Height);

        // vẽ chấm phản hồi tức thì.
        double r = 6;
        var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(Color.FromArgb(0x80, 0x3D, 0x7E, 0xFF)) };
        Canvas.SetLeft(dot, p.X - r);
        Canvas.SetTop(dot, p.Y - r);
        brushOverlay.Children.Add(dot);

        _developPanel?.AppendBrushPoint(nx, ny);
    }
}
