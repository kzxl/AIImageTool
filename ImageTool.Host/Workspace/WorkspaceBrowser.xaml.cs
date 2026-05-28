using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public partial class WorkspaceBrowser : UserControl, System.ComponentModel.INotifyPropertyChanged
{
    private IWorkspaceService? _workspace;
    private IThumbnailService? _thumbs;
    private IImageMetaService? _meta;

    public ObservableCollection<FolderNode> Roots { get; } = new();

    private ObservableCollection<ThumbItem> _thumbnails = new();
    public ObservableCollection<ThumbItem> Thumbnails
    {
        get => _thumbnails;
        private set
        {
            _thumbnails = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Thumbnails)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceBrowser()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoaded;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Roots.Count > 0) return;
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.IsReady) Roots.Add(new FolderNode(d.RootDirectory.FullName));
            }
        }
        catch { }
    }

    private void OnFolderOpened(object? sender, FolderOpenedEventArgs e)
    {
        // Build list off-thread, swap collection trên UI 1 lần để chỉ raise 1 reset event.
        var paths = e.Images.ToList();
        var meta = _meta;
        var thumbs = _thumbs;

        Task.Run(() =>
        {
            var items = new List<ThumbItem>(paths.Count);
            foreach (var p in paths)
            {
                var item = new ThumbItem(p);
                if (meta != null) item.ApplyMeta(meta.Get(p));
                var cached = thumbs?.TryGetThumbnailPath(p, 256);
                if (cached != null) item.SetThumb(cached);
                items.Add(item);
            }
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Thumbnails = new ObservableCollection<ThumbItem>(items);
            });
        });
    }

    private void OnThumbReady(object? sender, ThumbnailReadyEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in Thumbnails)
            {
                if (string.Equals(t.ImagePath, e.ImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    t.SetThumb(e.ThumbnailPath);
                    break;
                }
            }
        });
    }

    private void OnMetaChanged(object? sender, ImageMetaChangedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in Thumbnails)
            {
                if (string.Equals(t.ImagePath, e.ImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    t.ApplyMeta(e.Meta);
                    break;
                }
            }
        });
    }

    private void OnActiveChanged(object? sender, ImageSelectedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var t in Thumbnails)
            {
                t.IsActive = string.Equals(t.ImagePath, e.CurrentPath, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private void OnSelectionChanged(object? sender, BatchSelectionChangedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var set = new HashSet<string>(e.Selection, StringComparer.OrdinalIgnoreCase);
            foreach (var t in Thumbnails) t.IsSelected = set.Contains(t.ImagePath);
        });
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode fn && _workspace != null)
        {
            _workspace.OpenFolder(fn.Path);
        }
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is FolderNode fn)
        {
            fn.LoadChildren();
            e.Handled = true;
        }
    }

    private void Thumbnail_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ThumbItem item && _workspace != null)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (ctrl)
            {
                if (_workspace.Selection.Contains(item.ImagePath)) _workspace.RemoveFromSelection(item.ImagePath);
                else _workspace.AddToSelection(item.ImagePath);
            }
            else if (shift && _workspace.ActiveImage != null)
            {
                int from = Thumbnails.IndexOf(Thumbnails.FirstOrDefault(t => t.ImagePath == _workspace.ActiveImage)!);
                int to = Thumbnails.IndexOf(item);
                if (from >= 0 && to >= 0)
                {
                    int a = Math.Min(from, to), b = Math.Max(from, to);
                    _workspace.SetSelection(Thumbnails.Skip(a).Take(b - a + 1).Select(t => t.ImagePath));
                }
            }
            else
            {
                _workspace.SetSelection(new[] { item.ImagePath });
            }
            _workspace.SetActiveImage(item.ImagePath);
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_workspace == null) return;
        _workspace.Filter.Search = txtSearch.Text;
        _workspace.ApplyFilterAndSort();
    }

    private void CmbMinRating_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace == null || cmbMinRating == null) return;
        _workspace.Filter.MinRating = cmbMinRating.SelectedIndex; // 0..5
        _workspace.ApplyFilterAndSort();
    }

    private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace == null || cmbSort == null) return;
        if (cmbSort.SelectedItem is ComboBoxItem item && item.Tag is string tag &&
            Enum.TryParse<WorkspaceSort>(tag, out var s))
        {
            _workspace.Sort = s;
            _workspace.ApplyFilterAndSort();
        }
    }

    private void LabelFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null) return;
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            if (tag == "None") _workspace.Filter.RequiredLabel = null;
            else if (Enum.TryParse<ColorLabel>(tag, out var cl))
            {
                _workspace.Filter.RequiredLabel = _workspace.Filter.RequiredLabel == cl ? null : cl;
            }
            _workspace.ApplyFilterAndSort();
        }
    }
}

public class FolderNode : System.ComponentModel.INotifyPropertyChanged
{
    public string Path { get; }
    public string Name { get; }
    public ObservableCollection<FolderNode> Children { get; } = new();
    private bool _loaded;

    public FolderNode(string path)
    {
        Path = path;
        Name = string.IsNullOrEmpty(System.IO.Path.GetFileName(path)) ? path : System.IO.Path.GetFileName(path);
        Children.Add(new FolderNode(string.Empty) { _placeholder = true });
    }

    private bool _placeholder;
    public bool IsPlaceholder => _placeholder;

    public void LoadChildren()
    {
        if (_loaded) return;
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Path))
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    if ((di.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    Children.Add(new FolderNode(dir));
                }
                catch { }
            }
        }
        catch { }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class ThumbItem : System.ComponentModel.INotifyPropertyChanged
{
    public string ImagePath { get; }
    public string FileName { get; }

    private BitmapImage? _thumb;
    private int _rating;
    private ColorLabel _label;
    private PickFlag _pick;
    private bool _isSelected;
    private bool _isActive;

    public BitmapImage? Thumb { get => _thumb; private set { _thumb = value; Raise(nameof(Thumb)); } }
    public int Rating { get => _rating; set { if (_rating == value) return; _rating = value; Raise(nameof(Rating), nameof(RatingDisplay)); } }
    public ColorLabel Label { get => _label; set { if (_label == value) return; _label = value; Raise(nameof(Label), nameof(LabelBrush)); } }
    public PickFlag Pick { get => _pick; set { if (_pick == value) return; _pick = value; Raise(nameof(Pick), nameof(PickDisplay)); } }
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; Raise(nameof(IsSelected)); } }
    public bool IsActive { get => _isActive; set { if (_isActive == value) return; _isActive = value; Raise(nameof(IsActive)); } }

    public string RatingDisplay => _rating > 0 ? new string('★', _rating) : "";
    public string PickDisplay => _pick switch { PickFlag.Pick => "✓", PickFlag.Reject => "✗", _ => "" };

    public Brush LabelBrush => _label switch
    {
        ColorLabel.Red => Brushes.Red,
        ColorLabel.Yellow => Brushes.Gold,
        ColorLabel.Green => Brushes.LimeGreen,
        ColorLabel.Blue => Brushes.DodgerBlue,
        ColorLabel.Purple => Brushes.MediumPurple,
        _ => Brushes.Transparent
    };

    public ThumbItem(string path)
    {
        ImagePath = path;
        FileName = System.IO.Path.GetFileName(path);
    }

    public void ApplyMeta(ImageMeta m)
    {
        Rating = m.Rating;
        Label = m.Label;
        Pick = m.Pick;
    }

    public void SetThumb(string thumbPath)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(thumbPath);
            bmp.DecodePixelWidth = 256;
            bmp.EndInit();
            bmp.Freeze();
            Thumb = bmp;
        }
        catch { }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void Raise(params string[] props)
    {
        var h = PropertyChanged;
        if (h == null) return;
        foreach (var p in props) h(this, new System.ComponentModel.PropertyChangedEventArgs(p));
    }
}
