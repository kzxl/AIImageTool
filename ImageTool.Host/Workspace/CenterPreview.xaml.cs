using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public enum LighttableMode { Single, Grid, Cull, Full }

public partial class CenterPreview : UserControl
{
    private IWorkspaceService? _workspace;
    private IThumbnailService? _thumbs;
    private IImageMetaService? _meta;
    private LighttableMode _mode = LighttableMode.Single;

    public ObservableCollection<ThumbItem> GridItems { get; private set; } = new();

    public CenterPreview()
    {
        InitializeComponent();
        icGrid.ItemsSource = GridItems;
        Focusable = true;
    }

    public void Bind(IWorkspaceService workspace, IThumbnailService? thumbs = null, IImageMetaService? meta = null)
    {
        _workspace = workspace;
        _thumbs = thumbs;
        _meta = meta;
        _workspace.ActiveImageChanged += OnActiveChanged;
        _workspace.SelectionChanged += OnSelectionChanged;
        _workspace.FolderOpened += OnFolderOpened;
        if (_thumbs != null) _thumbs.ThumbnailReady += OnThumbReady;
        if (_meta != null) _meta.MetaChanged += OnMetaChanged;
    }

    private void OnFolderOpened(object? sender, FolderOpenedEventArgs e)
    {
        var paths = e.Images.ToList();
        var meta = _meta;
        var thumbs = _thumbs;
        Task.Run(() =>
        {
            var list = new List<ThumbItem>(paths.Count);
            foreach (var p in paths)
            {
                var item = new ThumbItem(p);
                if (meta != null) item.ApplyMeta(meta.Get(p));
                var cached = thumbs?.TryGetThumbnailPath(p, 256);
                if (cached != null) item.SetThumb(cached);
                list.Add(item);
            }
            Dispatcher.BeginInvoke(() =>
            {
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

            UpdatePreview(e.CurrentPath);
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

    private void SetMode(LighttableMode m)
    {
        _mode = m;
        paneSingle.Visibility = m == LighttableMode.Single ? Visibility.Visible : Visibility.Collapsed;
        paneGrid.Visibility = m == LighttableMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        paneCull.Visibility = m == LighttableMode.Cull ? Visibility.Visible : Visibility.Collapsed;
        paneFull.Visibility = m == LighttableMode.Full ? Visibility.Visible : Visibility.Collapsed;
        if (m == LighttableMode.Cull) RebuildCullView();
    }

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
            case Key.Escape:
                if (_mode == LighttableMode.Full) { SetMode(LighttableMode.Single); e.Handled = true; }
                break;
        }
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
}
