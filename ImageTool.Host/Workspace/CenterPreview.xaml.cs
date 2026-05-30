using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public enum LighttableMode { Single, Grid, Cull, Full }

public partial class CenterPreview : UserControl, IImageToolHost
{
    private IWorkspaceService? _workspace;
    private IThumbnailService? _thumbs;
    private IImageMetaService? _meta;
    private IHistoryService? _history;
    private DevelopClipboard? _clipboard;
    private readonly DevelopRenderer _renderer = new();
    private LighttableMode _mode = LighttableMode.Single;
    private bool _isDraggingSplit;
    private double _splitPercent = 0.5;
    private bool _externalAfterActive; // true khi plugin (Upscaler...) đẩy ảnh "after"

    // Zoom/pan loupe state
    private double _zoom = 1.0;
    private bool _isPanning;
    private Point _panStartMouse;
    private double _panStartX, _panStartY;
    private const double MinZoom = 1.0;
    private const double MaxZoom = 8.0;

    public ObservableCollection<ThumbItem> GridItems { get; private set; } = new();

    public DevelopRenderer Renderer => _renderer;

    public CenterPreview()
    {
        InitializeComponent();
        icGrid.ItemsSource = GridItems;
        Focusable = true;
        paneSingle.SizeChanged += (_, _) => { if (_cropMode) DrawCropOverlay(); };
    }

    public void Bind(IWorkspaceService workspace, IThumbnailService? thumbs = null, IImageMetaService? meta = null, IHistoryService? history = null)
    {
        _workspace = workspace;
        _thumbs = thumbs;
        _meta = meta;
        _history = history;
        _workspace.ActiveImageChanged += OnActiveChanged;
        _workspace.SelectionChanged += OnSelectionChanged;
        _workspace.FolderOpened += OnFolderOpened;
        if (_thumbs != null) _thumbs.ThumbnailReady += OnThumbReady;
        if (_meta != null) _meta.MetaChanged += OnMetaChanged;
        if (_history != null) _history.HistoryChanged += OnHistoryChanged;
    }

    /// <summary>Cấp DevelopClipboard cho context menu trên grid (gọi sau Bind).</summary>
    public void BindContext(DevelopClipboard clipboard) => _clipboard = clipboard;

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e)
    {
        // Chỉ re-render nếu ảnh đang xem khớp ảnh có history vừa đổi.
        var active = _workspace?.ActiveImage;
        // Cập nhật badge "đã chỉnh sửa" cho thumbnail tương ứng (mọi ảnh, không chỉ ảnh active).
        bool edited = (_history?.GetPointer(e.ImagePath) ?? 0) > 0;
        foreach (var t in GridItems)
            if (string.Equals(t.ImagePath, e.ImagePath, StringComparison.OrdinalIgnoreCase))
            { t.IsEdited = edited; break; }

        if (string.IsNullOrEmpty(active)) return;
        if (!string.Equals(active, e.ImagePath, StringComparison.OrdinalIgnoreCase)) return;
        if (_externalAfterActive) return; // đang so sánh kết quả plugin, đừng đè
        _ = RenderDevelopAsync(active);
    }

    private void OnFolderOpened(object? sender, FolderOpenedEventArgs e)
    {
        var paths = e.Images.ToList();
        var meta = _meta;
        var thumbs = _thumbs;
        var history = _history;
        Task.Run(() =>
        {
            var list = new List<ThumbItem>(paths.Count);
            foreach (var p in paths)
            {
                var item = new ThumbItem(p);
                if (meta != null) item.ApplyMeta(meta.Get(p));
                if (history != null) item.IsEdited = history.GetPointer(p) > 0;
                var cached = thumbs?.TryGetThumbnailPath(p, 256);
                if (cached != null) item.SetThumb(cached);
                list.Add(item);
            }
            Dispatcher.BeginInvoke(() =>
            {
                _stacked = false;
                _allGridBackup = null;
                btnStack.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30));
                GridItems = new ObservableCollection<ThumbItem>(list);
                icGrid.ItemsSource = GridItems;
            });
        });
    }

    private void OnThumbReady(object? sender, ThumbnailReadyEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in GridItems)
                if (string.Equals(t.ImagePath, e.ImagePath, StringComparison.OrdinalIgnoreCase))
                { t.SetThumb(e.ThumbnailPath); break; }
        });
    }

    private void OnMetaChanged(object? sender, ImageMetaChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in GridItems)
                if (string.Equals(t.ImagePath, e.ImagePath, StringComparison.OrdinalIgnoreCase))
                { t.ApplyMeta(e.Meta); break; }
        });
    }

    private void OnActiveChanged(object? sender, ImageSelectedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in GridItems)
                t.IsActive = string.Equals(t.ImagePath, e.CurrentPath, StringComparison.OrdinalIgnoreCase);

            ClearResult(); // ảnh đổi → xoá after
            ResetZoom();   // ảnh đổi → về fit
            UpdatePreview(e.CurrentPath);
            ActiveImageChanged?.Invoke(this, e.CurrentPath);
        });
    }

    private void OnSelectionChanged(object? sender, BatchSelectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            txtSelection.Text = $"Selection: {e.Selection.Count}";
            var set = new HashSet<string>(e.Selection, StringComparer.OrdinalIgnoreCase);
            foreach (var t in GridItems) t.IsSelected = set.Contains(t.ImagePath);
            if (_mode == LighttableMode.Cull) RebuildCullView();
        });
    }

    private void UpdatePreview(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            imgPreview.Source = null;
            imgFull.Source = null;
            txtPlaceholder.Visibility = Visibility.Visible;
            txtFile.Text = "(chưa chọn ảnh)";
            txtMeta.Text = "";
            return;
        }

        // Nếu ảnh có history chỉnh sửa và decoder hỗ trợ, render qua pipeline non-destructive.
        int pointer = _history?.GetPointer(path) ?? 0;
        if (pointer > 0 && _renderer.CanDecode(path))
        {
            _ = RenderDevelopAsync(path);
            return;
        }

        // RAW: WPF BitmapImage không đọc được -> render qua pipeline (trích JPEG preview nhúng).
        if (ImageTool.Imaging.RawPreviewExtractor.IsRawExtension(path) && _renderer.CanDecode(path))
        {
            _ = RenderDevelopAsync(path);
            return;
        }

        // Mặc định: hiển thị nhanh bằng BitmapImage (proxy decode width để tiết kiệm RAM).
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            imgPreview.Source = bmp;
            imgFull.Source = bmp;
            txtPlaceholder.Visibility = Visibility.Collapsed;
            txtFile.Text = Path.GetFileName(path);
            var fi = new FileInfo(path);
            txtMeta.Text = $"{bmp.PixelWidth} x {bmp.PixelHeight}  |  {fi.Length / 1024.0:N0} KB";
        }
        catch
        {
            imgPreview.Source = null;
            imgFull.Source = null;
            txtPlaceholder.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Render ảnh qua pipeline Develop (proxy linear-light) và đẩy lên preview.</summary>
    private async Task RenderDevelopAsync(string path)
    {
        var history = _history;
        var ops = history?.GetStack(path) ?? (IReadOnlyList<EditOperation>)Array.Empty<EditOperation>();
        int pointer = history?.GetPointer(path) ?? 0;

        // Trong crop mode: hiển thị ảnh CHƯA cắt (bỏ rectangle, giữ straighten) để overlay khớp toạ độ.
        if (_cropMode) ops = StripCropRect(ops, pointer);

        try
        {
            var bmp = await _renderer.RenderPreviewAsync(path, ops, pointer);
            if (bmp == null) return; // bị hủy bởi job mới hơn
            // Chỉ áp nếu ảnh đang xem vẫn là ảnh này.
            if (!string.Equals(_workspace?.ActiveImage, path, StringComparison.OrdinalIgnoreCase)) return;
            imgPreview.Source = bmp;
            imgFull.Source = bmp;
            txtPlaceholder.Visibility = Visibility.Collapsed;
            txtFile.Text = Path.GetFileName(path);
            txtMeta.Text = $"{bmp.PixelWidth} x {bmp.PixelHeight}  |  edit · {pointer} bước";
            if (_cropMode) DrawCropOverlay();
            RefreshClipOverlayIfActive();
        }
        catch { }
    }

    private void SetMode(LighttableMode m) => SwitchMode(m);

    public void SwitchMode(LighttableMode m)
    {
        _mode = m;
        // Rời compare mode khi chọn mode khác (compare là lớp phủ riêng).
        if (_compareMode)
        {
            _compareMode = false;
            paneCompare.Visibility = Visibility.Collapsed;
            imgCompareBefore.Source = null;
            imgCompareAfter.Source = null;
            btnCompare.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30));
        }
        paneSingle.Visibility = m == LighttableMode.Single ? Visibility.Visible : Visibility.Collapsed;
        paneGrid.Visibility = m == LighttableMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        paneCull.Visibility = m == LighttableMode.Cull ? Visibility.Visible : Visibility.Collapsed;
        paneFull.Visibility = m == LighttableMode.Full ? Visibility.Visible : Visibility.Collapsed;
        if (m == LighttableMode.Cull) RebuildCullView();
        ModeChanged?.Invoke(this, m);
    }

    public LighttableMode CurrentMode => _mode;
    public event EventHandler<LighttableMode>? ModeChanged;

    private void RebuildCullView()
    {
        paneCull.Children.Clear();
        if (_workspace == null) return;
        var sel = _workspace.Selection.Take(4).ToList();
        if (sel.Count == 0 && _workspace.ActiveImage != null) sel.Add(_workspace.ActiveImage);
        if (sel.Count == 0) return;

        paneCull.Columns = sel.Count <= 1 ? 1 : (sel.Count <= 2 ? 2 : 2);
        paneCull.Rows = sel.Count <= 2 ? 1 : 2;

        foreach (var p in sel)
        {
            var img = new Image { Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(4) };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(p);
                bmp.DecodePixelWidth = 1600;
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
            }
            catch { }
            var border = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Child = img
            };
            paneCull.Children.Add(border);
        }
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.G: SetMode(LighttableMode.Grid); e.Handled = true; break;
            case Key.E: SetMode(LighttableMode.Single); e.Handled = true; break;
            case Key.C: SetMode(LighttableMode.Cull); e.Handled = true; break;
            case Key.F: SetMode(LighttableMode.Full); e.Handled = true; break;
            case Key.R: ToggleCropMode(); e.Handled = true; break;
            case Key.J: ToggleClipOverlay(); e.Handled = true; break;
            case Key.Y: // Y: bật/tắt so sánh before/after cạnh nhau (không khi giữ Ctrl = redo)
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) { ToggleCompareMode(); e.Handled = true; }
                break;
            case Key.Oem5: // phím "\" : xem ảnh gốc (before) khi giữ
                if (!_showingBefore) ShowBefore(true);
                e.Handled = true;
                break;
            case Key.Z: // toggle 100% / fit (như Lightroom)
                ToggleZoom();
                e.Handled = true;
                break;
            case Key.OemPlus:
            case Key.Add:
                StepZoom(1.25); e.Handled = true; break;
            case Key.OemMinus:
            case Key.Subtract:
                StepZoom(1 / 1.25); e.Handled = true; break;
            case Key.Escape:
                if (_zoom > 1.0) { ResetZoom(); e.Handled = true; }
                else if (_mode == LighttableMode.Full) { SetMode(LighttableMode.Single); e.Handled = true; }
                break;
        }
    }

    private void UserControl_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Oem5 && _showingBefore) { ShowBefore(false); e.Handled = true; }
    }

    private bool _showingBefore;

    /// <summary>Tạm hiện ảnh GỐC (before) khi giữ phím "\"; nhả ra hiện lại bản đã chỉnh.</summary>
    private void ShowBefore(bool before)
    {
        _showingBefore = before;
        var path = _workspace?.ActiveImage;
        if (string.IsNullOrEmpty(path) || _externalAfterActive) { _showingBefore = false; return; }
        if (before)
        {
            // render pointer=0 (ảnh gốc).
            _ = RenderAtPointerAsync(path, 0);
            txtFile.Text = System.IO.Path.GetFileName(path) + "  [BEFORE]";
        }
        else
        {
            _ = RenderDevelopAsync(path);
        }
    }

    private async Task RenderAtPointerAsync(string path, int pointer)
    {
        if (!_renderer.CanDecode(path)) return;
        try
        {
            var ops = _history?.GetStack(path) ?? (IReadOnlyList<EditOperation>)Array.Empty<EditOperation>();
            var bmp = await _renderer.RenderPreviewAsync(path, ops, pointer);
            if (bmp == null) return;
            if (!string.Equals(_workspace?.ActiveImage, path, StringComparison.OrdinalIgnoreCase)) return;
            imgPreview.Source = bmp;
            imgFull.Source = bmp;
        }
        catch { }
    }

    private void BtnModeSingle_Click(object sender, RoutedEventArgs e) => SetMode(LighttableMode.Single);
    private void BtnModeGrid_Click(object sender, RoutedEventArgs e) => SetMode(LighttableMode.Grid);
    private void BtnModeCull_Click(object sender, RoutedEventArgs e) => SetMode(LighttableMode.Cull);
    private void BtnModeFull_Click(object sender, RoutedEventArgs e) => SetMode(LighttableMode.Full);

    private void GridItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ThumbItem item && _workspace != null)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl)
            {
                if (_workspace.Selection.Contains(item.ImagePath)) _workspace.RemoveFromSelection(item.ImagePath);
                else _workspace.AddToSelection(item.ImagePath);
            }
            else
            {
                _workspace.SetSelection(new[] { item.ImagePath });
            }
            _workspace.SetActiveImage(item.ImagePath);
            if (e.ClickCount >= 2) SetMode(LighttableMode.Single);
        }
    }

    private void GridItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ThumbItem item &&
            _workspace != null && _meta != null && _history != null && _clipboard != null)
        {
            if (!_workspace.Selection.Contains(item.ImagePath))
            {
                _workspace.SetSelection(new[] { item.ImagePath });
                _workspace.SetActiveImage(item.ImagePath);
            }
            fe.ContextMenu = ImageContextMenu.Build(item.ImagePath, _workspace, _meta, _history, _clipboard);
            fe.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    // ===== Before/After splitter =====
    private void PaneSingle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TryHandleWbPick(e)) { e.Handled = true; return; }
        if (afterBadge.Visibility != Visibility.Visible) return;
        _isDraggingSplit = true;
        paneSingle.CaptureMouse();
        UpdateSplitFromPoint(e.GetPosition(paneSingle));
        e.Handled = true;
    }

    private void PaneSingle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var pos = e.GetPosition(paneSingle);
            zoomPan.X = _panStartX + (pos.X - _panStartMouse.X);
            zoomPan.Y = _panStartY + (pos.Y - _panStartMouse.Y);
            ClampPan();
            SyncAfterTransform();
            return;
        }
        if (!_isDraggingSplit) return;
        UpdateSplitFromPoint(e.GetPosition(paneSingle));
    }

    private void PaneSingle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSplit) return;
        _isDraggingSplit = false;
        paneSingle.ReleaseMouseCapture();
    }

    // ===== Zoom / Pan loupe =====
    private void PaneSingle_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (imgPreview.Source == null) return;
        double old = _zoom;
        double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(_zoom - old) < 1e-6) return;

        // Zoom quanh vị trí con trỏ: giữ điểm dưới chuột cố định.
        var p = e.GetPosition(imgPreview);
        double scaleRatio = _zoom / old;
        zoomPan.X = (zoomPan.X - p.X) * scaleRatio + p.X;
        zoomPan.Y = (zoomPan.Y - p.Y) * scaleRatio + p.Y;
        zoomScale.ScaleX = zoomScale.ScaleY = _zoom;
        ClampPan();
        SyncAfterTransform();
        UpdateZoomBadge();
        e.Handled = true;
    }

    private void PaneSingle_PanStart(object sender, MouseButtonEventArgs e)
    {
        if (_zoom <= 1.0 || imgPreview.Source == null) return;
        _isPanning = true;
        _panStartMouse = e.GetPosition(paneSingle);
        _panStartX = zoomPan.X;
        _panStartY = zoomPan.Y;
        paneSingle.CaptureMouse();
        paneSingle.Cursor = System.Windows.Input.Cursors.ScrollAll;
        e.Handled = true;
    }

    private void PaneSingle_PanEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        paneSingle.ReleaseMouseCapture();
        paneSingle.Cursor = System.Windows.Input.Cursors.Arrow;
        e.Handled = true;
    }

    private void ResetZoom()
    {
        _zoom = 1.0;
        zoomScale.ScaleX = zoomScale.ScaleY = 1.0;
        zoomPan.X = zoomPan.Y = 0;
        SyncAfterTransform();
        UpdateZoomBadge();
    }

    /// <summary>Toggle giữa fit (1.0) và 100% pixel-thực (zoom = full/fit ratio), zoom quanh tâm.</summary>
    private void ToggleZoom()
    {
        if (imgPreview.Source == null) return;
        if (_zoom > 1.001) { ResetZoom(); return; }
        // ước lượng zoom để đạt 100% pixel thực.
        double target = 2.0;
        if (imgPreview.Source is System.Windows.Media.Imaging.BitmapSource bs && imgPreview.ActualWidth > 0)
        {
            double fitW = imgPreview.ActualWidth;
            target = Math.Clamp(bs.PixelWidth / fitW, MinZoom, MaxZoom);
        }
        ZoomToCenter(target);
    }

    private void StepZoom(double factor)
    {
        if (imgPreview.Source == null) return;
        ZoomToCenter(Math.Clamp(_zoom * factor, MinZoom, MaxZoom));
    }

    private void ZoomToCenter(double newZoom)
    {
        double old = _zoom;
        _zoom = newZoom;
        var c = new Point(imgPreview.ActualWidth / 2, imgPreview.ActualHeight / 2);
        double ratio = _zoom / old;
        zoomPan.X = (zoomPan.X - c.X) * ratio + c.X;
        zoomPan.Y = (zoomPan.Y - c.Y) * ratio + c.Y;
        zoomScale.ScaleX = zoomScale.ScaleY = _zoom;
        ClampPan();
        SyncAfterTransform();
        UpdateZoomBadge();
    }

    private void ClampPan()
    {
        // Giới hạn pan để ảnh không trôi hoàn toàn khỏi khung.
        double w = imgPreview.ActualWidth, h = imgPreview.ActualHeight;
        if (w <= 0 || h <= 0) return;
        double maxX = w * (_zoom - 1);
        double maxY = h * (_zoom - 1);
        zoomPan.X = Math.Clamp(zoomPan.X, -maxX, 0);
        zoomPan.Y = Math.Clamp(zoomPan.Y, -maxY, 0);
    }

    private void SyncAfterTransform()
    {
        // Đồng bộ transform cho ảnh "after" để splitter so sánh đúng vùng.
        zoomScaleAfter.ScaleX = zoomScale.ScaleX;
        zoomScaleAfter.ScaleY = zoomScale.ScaleY;
        zoomPanAfter.X = zoomPan.X;
        zoomPanAfter.Y = zoomPan.Y;
        if (_clipOverlay) SyncClipTransform();
    }

    private void UpdateZoomBadge()
    {
        if (_zoom > 1.001)
        {
            txtZoom.Text = $"{_zoom * 100:0}%";
            zoomBadge.Visibility = Visibility.Visible;
        }
        else zoomBadge.Visibility = Visibility.Collapsed;
    }

    private void UpdateSplitFromPoint(Point p)
    {
        double w = paneSingle.ActualWidth;
        if (w <= 0) return;
        _splitPercent = Math.Clamp(p.X / w, 0, 1);
        UpdateSplitClip();
    }

    private void UpdateSplitClip()
    {
        if (imgAfter.Source == null) { imgAfter.Clip = null; borderSplitLine.Margin = new Thickness(0); return; }
        double w = paneSingle.ActualWidth;
        double h = paneSingle.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double clipXScreen = w * _splitPercent; // vị trí đường split trong toạ độ paneSingle

        // imgAfter có Margin + RenderTransform (scale/pan). Clip áp trong toạ độ LOCAL của imgAfter
        // (trước transform), nên phải inverse-transform vị trí màn hình về local:
        //   screen = marginLeft + (local * scale + panX)  =>  local = (screen - marginLeft - panX) / scale
        double marginLeft = imgAfter.Margin.Left;
        double s = zoomScaleAfter.ScaleX > 1e-6 ? zoomScaleAfter.ScaleX : 1.0;
        double tx = zoomPanAfter.X;
        double localX = (clipXScreen - marginLeft - tx) / s;

        // Clip phủ toàn bộ vùng bên phải localX (dùng dải lớn để bao mọi mức zoom/letterbox).
        imgAfter.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(localX, -100000, 200000, 200000));
        borderSplitLine.Margin = new Thickness(clipXScreen, 0, 0, 0);
    }

    private void BtnClearAfter_Click(object sender, RoutedEventArgs e) => ClearResult();

    // ===== IImageToolHost =====
    public string? ActiveImagePath => _workspace?.ActiveImage;
    public event EventHandler<string?>? ActiveImageChanged;

    public void ShowResult(string? resultPath, byte[]? imageBytes = null)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                BitmapImage? bmp = null;
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    bmp = new BitmapImage();
                    using var ms = new MemoryStream(imageBytes);
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                else if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(resultPath);
                    bmp.EndInit();
                    bmp.Freeze();
                }
                if (bmp == null) return;

                imgAfter.Source = bmp;
                imgAfter.Visibility = Visibility.Visible;
                borderSplitLine.Visibility = Visibility.Visible;
                afterBadge.Visibility = Visibility.Visible;
                _externalAfterActive = true;
                _splitPercent = 0.5;
                SwitchMode(LighttableMode.Single);
                UpdateSplitClip();
            }
            catch { }
        });
    }

    public void ClearResult()
    {
        Dispatcher.BeginInvoke(() =>
        {
            imgAfter.Source = null;
            imgAfter.Visibility = Visibility.Collapsed;
            imgAfter.Clip = null;
            borderSplitLine.Visibility = Visibility.Collapsed;
            afterBadge.Visibility = Visibility.Collapsed;
            _externalAfterActive = false;
        });
    }

    public void ReportProgress(int percent, string? status = null)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Phát ra event để MainWindow hiển thị ở status bar; vẫn cập nhật txtMeta cục bộ.
            ProgressReported?.Invoke(this, (percent, status));
            if (percent < 0) { txtMeta.Text = ""; return; }
            if (status != null) txtMeta.Text = $"{status} {percent}%";
        });
    }

    /// <summary>Phát tiến trình (percent, status) cho host hiển thị ở status bar. percent&lt;0 = ẩn.</summary>
    public event EventHandler<(int Percent, string? Status)>? ProgressReported;
}
