using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ImageTool.Host.Workspace;

public partial class CenterPreview
{
    private DevelopPanel? _developPanel;
    private bool _cropMode;
    private float _cropX, _cropY, _cropW = 1f, _cropH = 1f;
    private string _cropDragHandle = "";
    private Point _cropDragStart;
    private (float X, float Y, float W, float H) _cropAtDragStart;
    private const double HandleSize = 12;
    // Guide overlay khi crop: 0=Thirds, 1=Golden ratio, 2=Diagonals, 3=Grid, 4=None.
    private int _cropGuide;

    /// <summary>Đổi kiểu lưới guide crop (phím O kiểu Lightroom). Chỉ tác dụng khi đang crop.</summary>
    public void CycleCropGuide()
    {
        _cropGuide = (_cropGuide + 1) % 5;
        if (_cropMode) DrawCropOverlay();
    }

    /// <summary>Liên kết DevelopPanel để đồng bộ crop rectangle 2 chiều.</summary>
    public void BindCropPanel(DevelopPanel panel)
    {
        _developPanel = panel;
        _developPanel.CropChanged += (s, c) =>
        {
            _cropX = c.X; _cropY = c.Y; _cropW = c.W; _cropH = c.H;
            if (_cropMode) DrawCropOverlay();
        };
        // Nạp preset tỉ lệ vào combobox 1 lần.
        if (cmbCropRatio.Items.Count == 0)
        {
            foreach (var p in ImageTool.Imaging.CropAspect.Presets)
                cmbCropRatio.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = p.Name, Tag = p });
            cmbCropRatio.SelectedIndex = 0;
        }
    }

    private void CmbCropRatio_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_cropMode || cmbCropRatio.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        if (item.Tag is not ValueTuple<string, double, double> preset) return;
        if (imgPreview.Source is not BitmapSource bs) return;

        if (preset.Item2 <= 0 || preset.Item3 <= 0)
        {
            // Original / Free: full khung.
            _cropX = 0; _cropY = 0; _cropW = 1f; _cropH = 1f;
        }
        else
        {
            var r = ImageTool.Imaging.CropAspect.Centered(bs.PixelWidth, bs.PixelHeight, preset.Item2, preset.Item3);
            _cropX = r.X; _cropY = r.Y; _cropW = r.W; _cropH = r.H;
        }
        DrawCropOverlay();
        _developPanel?.SetCropRect(_cropX, _cropY, _cropW, _cropH);
    }

    private void BtnCrop_Click(object sender, RoutedEventArgs e) => ToggleCropMode();
    private void ToggleCropMode()
    {
        if (imgPreview.Source == null) return;
        var path = _workspace?.ActiveImage;
        if (string.IsNullOrEmpty(path) || !_renderer.CanDecode(path)) return;

        _cropMode = !_cropMode;
        if (_cropMode)
        {
            _developPanel?.DisableTat();
            SetMode(LighttableMode.Single);
            ResetZoom();
            if (_developPanel != null)
            {
                var c = _developPanel.GetCropRect();
                _cropX = c.X; _cropY = c.Y; _cropW = c.W; _cropH = c.H;
            }
            cropOverlay.Visibility = Visibility.Visible;
            btnCrop.Background = ThemeManager.GetBrush("AccentBrush");
            cmbCropRatio.Visibility = Visibility.Visible;
            btnSmartCrop.Visibility = Visibility.Visible;
            // render ảnh chưa cắt rồi vẽ overlay (DrawCropOverlay được gọi cuối RenderDevelopAsync).
            _ = RenderDevelopAsync(path);
        }
        else
        {
            cropOverlay.Visibility = Visibility.Collapsed;
            cropOverlay.Children.Clear();
            cmbCropRatio.Visibility = Visibility.Collapsed;
            btnSmartCrop.Visibility = Visibility.Collapsed;
            btnCrop.Background = ThemeManager.GetBrush("BgHoverBrush");
            // render lại có áp crop.
            _ = RenderDevelopAsync(path);
        }
    }

    /// <summary>
    /// Smart Crop (content-aware): phân tích ảnh proxy, tìm khung cắt tốt nhất cho tỉ lệ đang chọn
    /// (vùng nổi bật theo saliency + skin + bias trung tâm), rồi gán vào crop rectangle.
    /// </summary>
    private void BtnSmartCrop_Click(object sender, RoutedEventArgs e)
    {
        if (!_cropMode) return;
        var path = _workspace?.ActiveImage;
        if (string.IsNullOrEmpty(path) || !_renderer.CanDecode(path)) return;

        // Tỉ lệ mục tiêu từ combo (Original/Free -> 0,0 = giữ tỉ lệ ảnh).
        double rw = 0, rh = 0;
        if (cmbCropRatio.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is ValueTuple<string, double, double> preset)
        {
            rw = preset.Item2; rh = preset.Item3;
        }

        try
        {
            var r = _renderer.AnalyzeSmartCrop(path, rw, rh);
            if (r == null) return;
            _cropX = r.Value.X; _cropY = r.Value.Y; _cropW = r.Value.W; _cropH = r.Value.H;
            DrawCropOverlay();
            _developPanel?.SetCropRect(_cropX, _cropY, _cropW, _cropH);
        }
        catch (Exception ex)
        {
            ImageTool.Shared.AppLog.Warn("CenterPreview.SmartCrop", $"{path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Bản sao chuỗi op nhưng đặt lại Crop rectangle về full khung (giữ Angle straighten),
    /// để preview hiển thị ảnh CHƯA cắt khi đang chỉnh crop (overlay khớp toạ độ ảnh đầy đủ).
    /// </summary>
    private static System.Collections.Generic.IReadOnlyList<ImageTool.Core.EditOperation> StripCropRect(
        System.Collections.Generic.IReadOnlyList<ImageTool.Core.EditOperation> ops, int pointer)
    {
        var result = new System.Collections.Generic.List<ImageTool.Core.EditOperation>(ops.Count);
        int n = Math.Min(pointer, ops.Count);
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (i < n && string.Equals(op.OpType, "Crop", StringComparison.OrdinalIgnoreCase))
            {
                var p = new System.Collections.Generic.Dictionary<string, string>(op.Params)
                {
                    ["x"] = "0", ["y"] = "0", ["w"] = "1", ["h"] = "1"
                };
                result.Add(new ImageTool.Core.EditOperation { PluginId = op.PluginId, OpType = op.OpType, Title = op.Title, Params = p });
            }
            else result.Add(op);
        }
        return result;
    }

    /// <summary>Hình chữ nhật của ảnh hiển thị (Uniform stretch + margin) trong toạ độ paneSingle.</summary>
    private Rect GetDisplayedImageRect()
    {
        if (imgPreview.Source is not BitmapSource bs) return Rect.Empty;
        double margin = imgPreview.Margin.Left;
        double availW = paneSingle.ActualWidth - margin * 2;
        double availH = paneSingle.ActualHeight - margin * 2;
        if (availW <= 0 || availH <= 0) return Rect.Empty;
        double imgAspect = bs.PixelWidth / (double)bs.PixelHeight;
        double boxAspect = availW / availH;
        double dispW, dispH;
        if (imgAspect > boxAspect) { dispW = availW; dispH = availW / imgAspect; }
        else { dispH = availH; dispW = availH * imgAspect; }
        double left = margin + (availW - dispW) / 2;
        double top = margin + (availH - dispH) / 2;
        return new Rect(left, top, dispW, dispH);
    }

    private void DrawCropOverlay()
    {
        cropOverlay.Children.Clear();
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0) return;

        double cl = img.Left + _cropX * img.Width;
        double ct = img.Top + _cropY * img.Height;
        double cw = _cropW * img.Width;
        double ch = _cropH * img.Height;

        // Lớp tối 4 vùng ngoài crop.
        var shade = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0));
        AddShade(img.Left, img.Top, img.Width, ct - img.Top, shade);                 // trên
        AddShade(img.Left, ct + ch, img.Width, img.Bottom - (ct + ch), shade);       // dưới
        AddShade(img.Left, ct, cl - img.Left, ch, shade);                            // trái
        AddShade(cl + cw, ct, img.Right - (cl + cw), ch, shade);                     // phải

        // Khung crop.
        var border = new Rectangle
        {
            Width = Math.Max(0, cw), Height = Math.Max(0, ch),
            Stroke = Brushes.White, StrokeThickness = 1.5
        };
        Canvas.SetLeft(border, cl);
        Canvas.SetTop(border, ct);
        cropOverlay.Children.Add(border);

        // Đường guide bố cục theo _cropGuide.
        var thin = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        DrawCropGuides(cl, ct, cw, ch, thin);

        // 8 tay nắm.
        AddHandle(cl, ct, "nw"); AddHandle(cl + cw / 2, ct, "n"); AddHandle(cl + cw, ct, "ne");
        AddHandle(cl, ct + ch / 2, "w"); AddHandle(cl + cw, ct + ch / 2, "e");
        AddHandle(cl, ct + ch, "sw"); AddHandle(cl + cw / 2, ct + ch, "s"); AddHandle(cl + cw, ct + ch, "se");
    }

    private void AddShade(double x, double y, double w, double h, Brush b)
    {
        if (w <= 0 || h <= 0) return;
        var r = new Rectangle { Width = w, Height = h, Fill = b, IsHitTestVisible = false };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
        cropOverlay.Children.Add(r);
    }

    /// <summary>Vẽ lưới guide bố cục theo _cropGuide (0=Thirds,1=Golden,2=Diagonals,3=Grid,4=None).</summary>
    private void DrawCropGuides(double cl, double ct, double cw, double ch, Brush stroke)
    {
        void V(double fx) => cropOverlay.Children.Add(new Line { X1 = cl + cw * fx, Y1 = ct, X2 = cl + cw * fx, Y2 = ct + ch, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false });
        void H(double fy) => cropOverlay.Children.Add(new Line { X1 = cl, Y1 = ct + ch * fy, X2 = cl + cw, Y2 = ct + ch * fy, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false });
        void Diag(double x1, double y1, double x2, double y2) => cropOverlay.Children.Add(
            new Line { X1 = cl + cw * x1, Y1 = ct + ch * y1, X2 = cl + cw * x2, Y2 = ct + ch * y2, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false });

        switch (_cropGuide)
        {
            case 0: // Thirds
                V(1.0 / 3); V(2.0 / 3); H(1.0 / 3); H(2.0 / 3);
                break;
            case 1: // Golden ratio (phi ~0.618)
                const double phi = 0.61803398875;
                V(1 - phi); V(phi); H(1 - phi); H(phi);
                break;
            case 2: // Diagonals (2 đường chéo)
                Diag(0, 0, 1, 1); Diag(1, 0, 0, 1);
                break;
            case 3: // Grid 4x4
                for (int i = 1; i < 4; i++) { V(i / 4.0); H(i / 4.0); }
                break;
            // 4 = None: không vẽ
        }
    }

    private void AddHandle(double cx, double cy, string tag)
    {
        var e = new Rectangle
        {
            Width = HandleSize, Height = HandleSize,
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, Tag = tag
        };
        Canvas.SetLeft(e, cx - HandleSize / 2);
        Canvas.SetTop(e, cy - HandleSize / 2);
        cropOverlay.Children.Add(e);
    }

    private string HitTestHandle(Point p)
    {
        var img = GetDisplayedImageRect();
        if (img.IsEmpty) return "";
        double cl = img.Left + _cropX * img.Width;
        double ct = img.Top + _cropY * img.Height;
        double cw = _cropW * img.Width, ch = _cropH * img.Height;
        (string tag, double hx, double hy)[] handles =
        {
            ("nw", cl, ct), ("n", cl + cw / 2, ct), ("ne", cl + cw, ct),
            ("w", cl, ct + ch / 2), ("e", cl + cw, ct + ch / 2),
            ("sw", cl, ct + ch), ("s", cl + cw / 2, ct + ch), ("se", cl + cw, ct + ch),
        };
        foreach (var (tag, hx, hy) in handles)
            if (Math.Abs(p.X - hx) <= HandleSize && Math.Abs(p.Y - hy) <= HandleSize)
                return tag;
        // bên trong khung -> di chuyển.
        if (p.X >= cl && p.X <= cl + cw && p.Y >= ct && p.Y <= ct + ch) return "move";
        return "";
    }

    private void CropOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_cropMode) return;
        var p = e.GetPosition(cropOverlay);
        _cropDragHandle = HitTestHandle(p);
        if (_cropDragHandle == "") return;
        _cropDragStart = p;
        _cropAtDragStart = (_cropX, _cropY, _cropW, _cropH);
        cropOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void CropOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_cropMode || _cropDragHandle == "") return;
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0) return;
        var p = e.GetPosition(cropOverlay);
        double dxN = (p.X - _cropDragStart.X) / img.Width;
        double dyN = (p.Y - _cropDragStart.Y) / img.Height;

        float x = _cropAtDragStart.X, y = _cropAtDragStart.Y, w = _cropAtDragStart.W, h = _cropAtDragStart.H;
        const float minSz = 0.05f;

        if (_cropDragHandle == "move")
        {
            x = (float)Math.Clamp(x + dxN, 0, 1 - w);
            y = (float)Math.Clamp(y + dyN, 0, 1 - h);
        }
        else
        {
            float right = x + w, bottom = y + h;
            if (_cropDragHandle.Contains('w')) x = (float)Math.Clamp(x + dxN, 0, right - minSz);
            if (_cropDragHandle.Contains('n')) y = (float)Math.Clamp(y + dyN, 0, bottom - minSz);
            if (_cropDragHandle.Contains('e')) right = (float)Math.Clamp(right + dxN, x + minSz, 1);
            if (_cropDragHandle.Contains('s')) bottom = (float)Math.Clamp(bottom + dyN, y + minSz, 1);
            w = right - x; h = bottom - y;
        }
        _cropX = x; _cropY = y; _cropW = w; _cropH = h;
        DrawCropOverlay();
        e.Handled = true;
    }

    private void CropOverlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_cropDragHandle == "") return;
        _cropDragHandle = "";
        cropOverlay.ReleaseMouseCapture();
        _developPanel?.SetCropRect(_cropX, _cropY, _cropW, _cropH);
        e.Handled = true;
    }
}
