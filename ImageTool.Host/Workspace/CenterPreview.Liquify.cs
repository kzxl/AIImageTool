using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

// Liquify/Warp handle-drag capture (D3.5). Bật từ DevelopPanel: kéo trên ảnh tạo 1 warp
// (tâm = điểm bấm, vector dịch = hướng kéo). Mỗi warp vẽ 1 mũi tên + vòng bán kính.
public partial class CenterPreview
{
    private DevelopPanel? _liquifyPanel;
    private bool _liquifyMode;
    private bool _liquifyDragging;
    private Point _liquifyStart;   // điểm bấm (toạ độ overlay)

    /// <summary>Liên kết DevelopPanel để bật/tắt liquify + vẽ lại handle khi đổi ảnh/kích thước.</summary>
    public void BindLiquifyPanel(DevelopPanel panel)
    {
        _liquifyPanel = panel;
        panel.LiquifyActivated += (_, on) =>
        {
            _liquifyMode = on;
            liquifyOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                if (_cropMode) ToggleCropMode();
                SetMode(LighttableMode.Single);
                ResetZoom();
            }
            DrawLiquifyOverlay();
        };
        panel.LiquifyChanged += (_, _) => { if (_liquifyMode) DrawLiquifyOverlay(); };
        paneSingle.SizeChanged += (_, _) => { if (_liquifyMode) DrawLiquifyOverlay(); };
    }

    private void LiquifyOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_liquifyMode || _liquifyPanel == null) return;
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0) return;
        var p = e.GetPosition(liquifyOverlay);
        if (p.X < img.Left || p.X > img.Right || p.Y < img.Top || p.Y > img.Bottom) return;
        _liquifyDragging = true;
        _liquifyStart = p;
        liquifyOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void LiquifyOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_liquifyDragging) return;
        var p = e.GetPosition(liquifyOverlay);
        DrawLiquifyOverlay();                  // handle đã commit
        DrawArrow(_liquifyStart, p, true);     // mũi tên đang kéo (preview)
    }

    private void LiquifyOverlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_liquifyDragging) return;
        _liquifyDragging = false;
        liquifyOverlay.ReleaseMouseCapture();
        e.Handled = true;

        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0) { DrawLiquifyOverlay(); return; }
        var p = e.GetPosition(liquifyOverlay);

        // Tâm = điểm bấm (chuẩn hoá theo từng trục); dịch = vector kéo theo CẠNH DÀI.
        float cx = (float)((_liquifyStart.X - img.Left) / img.Width);
        float cy = (float)((_liquifyStart.Y - img.Top) / img.Height);
        double diag = Math.Max(img.Width, img.Height);
        float dx = (float)((p.X - _liquifyStart.X) / diag);
        float dy = (float)((p.Y - _liquifyStart.Y) / diag);

        // Bỏ qua kéo quá ngắn (coi như click nhầm).
        if (Math.Abs(dx) < 1e-3f && Math.Abs(dy) < 1e-3f) { DrawLiquifyOverlay(); return; }

        _liquifyPanel?.AddWarp(Clamp01(cx), Clamp01(cy), dx, dy);
        DrawLiquifyOverlay();
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    /// <summary>Vẽ lại toàn bộ warp handle đã commit (tâm + mũi tên dịch + vòng bán kính).</summary>
    private void DrawLiquifyOverlay()
    {
        liquifyOverlay.Children.Clear();
        if (!_liquifyMode || _liquifyPanel == null) return;
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0) return;
        double diag = Math.Max(img.Width, img.Height);

        foreach (var wpt in _liquifyPanel.GetWarps())
        {
            double cxs = img.Left + wpt.Cx * img.Width;
            double cys = img.Top + wpt.Cy * img.Height;
            double r = wpt.Radius * diag;

            // vòng bán kính ảnh hưởng
            var ring = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0x3D, 0x7E, 0xFF)),
                StrokeThickness = 1,
            };
            System.Windows.Controls.Canvas.SetLeft(ring, cxs - r);
            System.Windows.Controls.Canvas.SetTop(ring, cys - r);
            liquifyOverlay.Children.Add(ring);

            // mũi tên dịch
            var to = new Point(cxs + wpt.Dx * diag, cys + wpt.Dy * diag);
            DrawArrow(new Point(cxs, cys), to, false);
        }
    }

    // Vẽ 1 mũi tên từ a -> b (preview=true: vàng cho lúc đang kéo; false: xanh đã commit).
    private void DrawArrow(Point a, Point b, bool preview)
    {
        var color = preview ? Color.FromRgb(0xFF, 0xC8, 0x3D) : Color.FromRgb(0x3D, 0x7E, 0xFF);
        var brush = new SolidColorBrush(color);

        var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = brush, StrokeThickness = 2 };
        liquifyOverlay.Children.Add(line);

        // chấm tâm
        const double dr = 4;
        var dot = new Ellipse { Width = dr * 2, Height = dr * 2, Fill = brush };
        System.Windows.Controls.Canvas.SetLeft(dot, a.X - dr);
        System.Windows.Controls.Canvas.SetTop(dot, a.Y - dr);
        liquifyOverlay.Children.Add(dot);

        // đầu mũi tên
        double dxv = b.X - a.X, dyv = b.Y - a.Y;
        double len = Math.Sqrt(dxv * dxv + dyv * dyv);
        if (len > 6)
        {
            double ux = dxv / len, uy = dyv / len;
            const double head = 9;
            var p1 = new Point(b.X - (ux * head) + (-uy * head * 0.5), b.Y - (uy * head) + (ux * head * 0.5));
            var p2 = new Point(b.X - (ux * head) - (-uy * head * 0.5), b.Y - (uy * head) - (ux * head * 0.5));
            var poly = new Polygon { Fill = brush, Points = new PointCollection { b, p1, p2 } };
            liquifyOverlay.Children.Add(poly);
        }
    }
}
