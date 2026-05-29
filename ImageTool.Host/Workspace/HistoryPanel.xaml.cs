using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public partial class HistoryPanel : UserControl
{
    private IWorkspaceService? _workspace;
    private IHistoryService? _history;

    public ObservableCollection<HistoryRow> Rows { get; } = new();

    public HistoryPanel()
    {
        InitializeComponent();
        icHistory.ItemsSource = Rows;
    }

    public void Bind(IWorkspaceService workspace, IHistoryService history)
    {
        _workspace = workspace;
        _history = history;
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
        });
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
}

public class HistoryRow
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public string Marker { get; set; } = "·";
    public string TimeShort { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsFuture { get; set; }
    public Brush TextBrush => IsFuture ? Brushes.DimGray : (IsActive ? Brushes.White : (Brush)new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
    public Brush MarkerBrush => IsFuture ? Brushes.DimGray : (IsActive ? Brushes.Gold : (Brush)new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));
}
