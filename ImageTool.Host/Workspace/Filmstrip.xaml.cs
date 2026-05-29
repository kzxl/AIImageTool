using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public partial class Filmstrip : UserControl
{
    private IWorkspaceService? _workspace;
    private IThumbnailService? _thumbs;
    private IImageMetaService? _meta;
    private IHistoryService? _history;
    private DevelopClipboard? _clipboard;

    public ObservableCollection<ThumbItem> Items { get; private set; } = new();

    public Filmstrip()
    {
        InitializeComponent();
        icStrip.ItemsSource = Items;
    }

    public void Bind(IWorkspaceService workspace, IThumbnailService thumbs, IImageMetaService meta)
    {
        _workspace = workspace;
        _thumbs = thumbs;
        _meta = meta;
        _workspace.FolderOpened += OnFolderOpened;
        _workspace.ActiveImageChanged += OnActiveChanged;
        _workspace.SelectionChanged += OnSelectionChanged;
        _thumbs.ThumbnailReady += OnThumbReady;
        _meta.MetaChanged += OnMetaChanged;
    }

    /// <summary>Cấp service cho context menu (gọi sau Bind).</summary>
    public void BindContext(IHistoryService history, DevelopClipboard clipboard)
    {
        _history = history;
        _clipboard = clipboard;
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
                var cached = thumbs?.TryGetThumbnailPath(p, 128);
                if (cached != null) item.SetThumb(cached);
                list.Add(item);
            }
            Dispatcher.BeginInvoke(() =>
            {
                Items = new ObservableCollection<ThumbItem>(list);
                icStrip.ItemsSource = Items;
            });
        });
    }

    private void OnThumbReady(object? sender, ThumbnailReadyEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in Items)
                if (string.Equals(t.ImagePath, e.ImagePath, StringComparison.OrdinalIgnoreCase))
                { t.SetThumb(e.ThumbnailPath); break; }
        });
    }

    private void OnMetaChanged(object? sender, ImageMetaChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in Items)
                if (string.Equals(t.ImagePath, e.ImagePath, StringComparison.OrdinalIgnoreCase))
                { t.ApplyMeta(e.Meta); break; }
        });
    }

    private void OnActiveChanged(object? sender, ImageSelectedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ThumbItem? hit = null;
            foreach (var t in Items)
            {
                t.IsActive = string.Equals(t.ImagePath, e.CurrentPath, StringComparison.OrdinalIgnoreCase);
                if (t.IsActive) hit = t;
            }
            if (hit != null) ScrollIntoView(hit);
        });
    }

    private void OnSelectionChanged(object? sender, BatchSelectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var set = new HashSet<string>(e.Selection, StringComparer.OrdinalIgnoreCase);
            foreach (var t in Items) t.IsSelected = set.Contains(t.ImagePath);
        });
    }

    private void ScrollIntoView(ThumbItem item)
    {
        var idx = Items.IndexOf(item);
        if (idx < 0) return;
        // 90 width + 6 margin
        scroller.ScrollToHorizontalOffset(Math.Max(0, idx * 96 - scroller.ActualWidth / 2 + 48));
    }

    private void StripItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
        }
    }

    private void StripItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ThumbItem item &&
            _workspace != null && _meta != null && _history != null && _clipboard != null)
        {
            // Nếu ảnh chưa nằm trong selection -> chọn riêng nó (UX chuẩn).
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
}
