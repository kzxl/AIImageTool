using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

public partial class BatchQueuePanel : UserControl
{
    private IBatchService? _batch;
    public ObservableCollection<JobRow> Rows { get; } = new();

    public BatchQueuePanel()
    {
        InitializeComponent();
        icJobs.ItemsSource = Rows;
    }

    public void Bind(IBatchService batch)
    {
        _batch = batch;
        _batch.QueueChanged += (s, e) => Refresh();
        _batch.JobUpdated += (s, j) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_batch == null) return;
            // Đồng bộ Rows với _batch.Jobs (theo index, bảo toàn binding)
            var src = _batch.Jobs.ToList();
            for (int i = 0; i < src.Count; i++)
            {
                if (i < Rows.Count)
                {
                    Rows[i].UpdateFrom(src[i]);
                }
                else
                {
                    Rows.Add(new JobRow().UpdateFrom(src[i]));
                }
            }
            while (Rows.Count > src.Count) Rows.RemoveAt(Rows.Count - 1);
        });
    }

    private void BtnPauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_batch == null) return;
        if (_batch.IsPaused) _batch.Resume(); else _batch.Pause();
        btnPause.Content = _batch.IsPaused ? "▶" : "⏸";
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e) => _batch?.ClearCompleted();

    private void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id) _batch?.RetryJob(id);
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id) _batch?.RemoveJob(id);
    }

    private void CmbParallel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_batch == null || cmbParallel.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Content?.ToString(), out var n)) _batch.MaxParallel = n;
    }
}

public class JobRow : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public int Progress { get; private set; }
    public string StatusText { get; private set; } = "";
    public string StatusGlyph { get; private set; } = "·";
    public Brush StatusBrush { get; private set; } = ThemeManager.GetBrush("TextDimBrush");

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public JobRow UpdateFrom(BatchJob j)
    {
        Id = j.Id;
        DisplayName = j.DisplayName;
        Progress = j.Progress;
        StatusText = j.Status switch
        {
            BatchJobStatus.Pending => "queued",
            BatchJobStatus.Running => $"{j.Progress}%",
            BatchJobStatus.Completed => "done",
            BatchJobStatus.Failed => j.Error ?? "failed",
            BatchJobStatus.Canceled => "canceled",
            BatchJobStatus.Paused => "paused",
            _ => ""
        };
        (StatusGlyph, StatusBrush) = j.Status switch
        {
            BatchJobStatus.Running => ("●", Brushes.DodgerBlue),
            BatchJobStatus.Completed => ("✓", Brushes.LimeGreen),
            BatchJobStatus.Failed => ("✗", Brushes.IndianRed),
            BatchJobStatus.Canceled => ("✕", ThemeManager.GetBrush("TextDimBrush")),
            BatchJobStatus.Pending => ("○", ThemeManager.GetBrush("TextDimBrush")),
            _ => ("·", ThemeManager.GetBrush("TextDimBrush"))
        };
        var h = PropertyChanged;
        if (h != null)
        {
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Id)));
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayName)));
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Progress)));
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusText)));
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusGlyph)));
            h(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusBrush)));
        }
        return this;
    }
}
