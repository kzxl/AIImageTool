using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public partial class HistoryPanel : UserControl
{
    private IWorkspaceService? _workspace;
    private IHistoryService? _history;
    private DevelopRenderer? _renderer;

    public ObservableCollection<HistoryRow> Rows { get; } = new();
    public ObservableCollection<SnapshotRow> Snapshots { get; } = new();

    public HistoryPanel()
    {
        InitializeComponent();
        icHistory.ItemsSource = Rows;
        icSnapshots.ItemsSource = Snapshots;
    }

    public void Bind(IWorkspaceService workspace, IHistoryService history, DevelopRenderer? renderer = null)
    {
        _workspace = workspace;
        _history = history;
        _renderer = renderer;
        _workspace.ActiveImageChanged += (s, e) => Refresh(e.CurrentPath);
        _history.HistoryChanged += (s, e) =>
        {
            if (string.Equals(e.ImagePath, _workspace.ActiveImage, StringComparison.OrdinalIgnoreCase))
                Refresh(_workspace.ActiveImage);
        };
    }

    private void Refresh(string? path)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Rows.Clear();
            Snapshots.Clear();
            if (_history == null || string.IsNullOrEmpty(path)) return;

            var stack = _history.GetStack(path);
            int ptr = _history.GetPointer(path);

            // Base step
            Rows.Add(new HistoryRow
            {
                Index = 0,
                Title = "Original",
                IsActive = ptr == 0,
                IsFuture = false,
                TimeShort = "",
                Marker = "○"
            });
            for (int i = 0; i < stack.Count; i++)
            {
                var op = stack[i];
                var future = (i + 1) > ptr;
                Rows.Add(new HistoryRow
                {
                    Index = i + 1,
                    Title = ImageTool.Shared.OpDisplayNames.Get(op.OpType, op.Title),
                    IsActive = (i + 1) == ptr,
                    IsFuture = future,
                    TimeShort = op.Timestamp.ToLocalTime().ToString("HH:mm"),
                    Marker = future ? "·" : "●"
                });
            }

            foreach (var s in _history.GetSnapshots(path))
                Snapshots.Add(new SnapshotRow
                {
                    Name = s.Name,
                    TimeShort = s.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm"),
                });

            // Thumbnail từng bước (11.11): render nền, gán dần khi xong (không chặn UI).
            RenderThumbnails(path, stack);
        });
    }

    /// <summary>Render thumbnail nhỏ cho từng mốc history (off-UI) rồi gán vào row tương ứng.</summary>
    private async void RenderThumbnails(string path, IReadOnlyList<EditOperation> stack)
    {
        if (_renderer == null || !_renderer.CanDecode(path)) return;
        var ops = new List<EditOperation>(stack);
        // Chụp lại danh sách row hiện tại để khớp index (Refresh có thể chạy lại).
        var rowsSnapshot = Rows.ToList();
        for (int i = 0; i < rowsSnapshot.Count; i++)
        {
            var row = rowsSnapshot[i];
            int pointer = row.Index;
            var bmp = await _renderer.RenderThumbnailAsync(path, ops, pointer, 44);
            // Nếu user đã đổi ảnh trong lúc render -> bỏ (row không còn trong Rows).
            if (bmp != null && Rows.Contains(row)) row.Thumb = bmp;
        }
    }

    private void HistoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryRow row && _workspace?.ActiveImage != null)
        {
            _history?.SetPointer(_workspace.ActiveImage, row.Index);
        }
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace?.ActiveImage != null) _history?.Undo(_workspace.ActiveImage);
    }
    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace?.ActiveImage != null) _history?.Redo(_workspace.ActiveImage);
    }
    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace?.ActiveImage != null) _history?.Clear(_workspace.ActiveImage);
    }

    private void BtnAddSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_history == null || _workspace?.ActiveImage == null)
        {
            MessageBox.Show("Hãy chọn 1 ảnh trước khi lưu snapshot.", "Snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int n = _history.GetSnapshots(_workspace.ActiveImage).Count + 1;
        var dlg = new InputDialog("Lưu Snapshot", "Tên snapshot:", $"Snapshot {n}");
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
            _history.SaveSnapshot(_workspace.ActiveImage, dlg.Result.Trim());
    }

    private void Snapshot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_history == null || _workspace?.ActiveImage == null) return;
        if (sender is FrameworkElement fe && fe.DataContext is SnapshotRow row)
            _history.ApplySnapshot(_workspace.ActiveImage, row.Name);
    }

    private void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_history == null || _workspace?.ActiveImage == null) return;
        if (sender is FrameworkElement fe && fe.Tag is string name)
            _history.DeleteSnapshot(_workspace.ActiveImage, name);
        e.Handled = true; // không lan ra Snapshot_Click (áp snapshot)
    }
}

public class SnapshotRow
{
    public string Name { get; set; } = "";
    public string TimeShort { get; set; } = "";
}

public class HistoryRow : INotifyPropertyChanged
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public string Marker { get; set; } = "·";
    public string TimeShort { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsFuture { get; set; }
    public Brush TextBrush => IsFuture ? ThemeManager.GetBrush("TextDimBrush") : (IsActive ? ThemeManager.GetBrush("TextPrimaryBrush") : ThemeManager.GetBrush("TextSecondaryBrush"));
    public Brush MarkerBrush => IsFuture ? ThemeManager.GetBrush("TextDimBrush") : (IsActive ? ThemeManager.GetBrush("AccentWarmBrush") : ThemeManager.GetBrush("TextDimBrush"));

    private BitmapSource? _thumb;
    public BitmapSource? Thumb
    {
        get => _thumb;
        set { _thumb = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumb))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
