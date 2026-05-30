using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ImageTool.Core;
using ImageTool.Shared;

namespace ImageTool.Host.Workspace;

public partial class StylePanel : UserControl
{
    private IStyleService? _styles;
    private IWorkspaceService? _workspace;
    private IBatchService? _batch;

    public ObservableCollection<StyleRow> Rows { get; } = new();

    public StylePanel()
    {
        InitializeComponent();
        icStyles.ItemsSource = Rows;
    }

    public void Bind(IStyleService styles, IWorkspaceService workspace, IBatchService batch)
    {
        _styles = styles;
        _workspace = workspace;
        _batch = batch;
        _styles.StylesChanged += (s, e) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Rows.Clear();
            if (_styles == null) return;
            foreach (var st in _styles.Styles)
            {
                Rows.Add(new StyleRow
                {
                    Id = st.Id,
                    Name = st.Name,
                    OpsSummary = $"{st.Operations.Count} ops · {st.CreatedAt.ToLocalTime():dd/MM HH:mm}"
                });
            }
        });
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_styles == null || _workspace?.ActiveImage == null)
        {
            MessageBox.Show("Hãy chọn 1 ảnh có history trước khi lưu Style.", "Style", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new InputDialog("Lưu Style", "Tên style:", $"Style {_styles.Styles.Count + 1}");
        if (dlg.ShowDialog() == true)
        {
            _styles.SaveFromHistory(dlg.Result, _workspace.ActiveImage);
        }
    }

    /// <summary>Import preset Lightroom (.xmp) -> Style (9.3).</summary>
    private void BtnImportXmp_Click(object sender, RoutedEventArgs e)
    {
        if (_styles == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Lightroom preset (.xmp)",
            Filter = "Lightroom XMP (*.xmp)|*.xmp|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var content = System.IO.File.ReadAllText(dlg.FileName);
            var ops = LightroomXmpImporter.Parse(content);
            if (ops.Count == 0)
            {
                MessageBox.Show("Không tìm thấy thiết lập Develop nào trong file XMP này.", "Import XMP",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            _styles.SaveFromOperations(name, ops, $"Imported from Lightroom · {ops.Count} ops");
            MessageBox.Show($"Đã nhập preset '{name}' ({ops.Count} op).", "Import XMP",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            ImageTool.Shared.AppLog.Error("StylePanel.ImportXmp", dlg.FileName, ex);
            MessageBox.Show($"Lỗi khi nhập XMP: {ex.Message}", "Import XMP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (_styles == null || _workspace == null || _batch == null) return;
        if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
        var style = _styles.Styles.FirstOrDefault(s => s.Id == id);
        if (style == null) return;

        var sel = _workspace.Selection.ToList();
        if (sel.Count == 0 && _workspace.ActiveImage != null) sel.Add(_workspace.ActiveImage);
        if (sel.Count == 0)
        {
            MessageBox.Show("Hãy chọn ảnh đích trước khi apply Style.", "Style", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var jobs = sel.Select(p => new BatchJob
        {
            PluginId = StyleBatchAdapter.Plugin,
            OpType = StyleBatchAdapter.OpApply,
            InputPath = p,
            Params = new Dictionary<string, string>
            {
                ["styleId"] = id,
                ["mode"] = chkAppend?.IsChecked == true ? "append" : "replace",
            }
        });
        _batch.EnqueueRange(jobs);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_styles == null) return;
        if (sender is FrameworkElement fe && fe.Tag is string id)
        {
            var style = _styles.Styles.FirstOrDefault(s => s.Id == id);
            if (style == null) return;
            if (MessageBox.Show($"Xóa style '{style.Name}'?", "Style", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                _styles.Delete(id);
        }
    }
}

public class StyleRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string OpsSummary { get; set; } = "";
}

public class InputDialog : Window
{
    private readonly TextBox _tb;
    public string Result { get; private set; } = "";

    public InputDialog(string title, string prompt, string defaultText)
    {
        Title = title;
        Width = 360; Height = 160;
        Background = System.Windows.Media.Brushes.Black;
        Foreground = System.Windows.Media.Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current.MainWindow;
        ResizeMode = ResizeMode.NoResize;

        var sp = new StackPanel { Margin = new Thickness(15) };
        sp.Children.Add(new TextBlock { Text = prompt, Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 0, 0, 6) });
        _tb = new TextBox { Text = defaultText, Padding = new Thickness(4, 2, 4, 2) };
        sp.Children.Add(_tb);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70 };
        ok.Click += (s, e) => { Result = _tb.Text; DialogResult = true; };
        cancel.Click += (s, e) => { DialogResult = false; };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        sp.Children.Add(buttons);
        Content = sp;
        _tb.SelectAll();
        _tb.Focus();
    }
}
