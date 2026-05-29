using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ImageTool.Core;
using ImageTool.Shared;

namespace ImageTool.Host.Workspace;

public partial class ExportPanel : UserControl
{
    private IBatchService? _batch;
    private IWorkspaceService? _workspace;

    public ExportPanel()
    {
        InitializeComponent();
        txtOutDir.Text = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Output");
    }

    public void Bind(IWorkspaceService workspace, IBatchService batch)
    {
        _workspace = workspace;
        _batch = batch;
        _workspace.SelectionChanged += (s, e) =>
            Dispatcher.BeginInvoke(() =>
            {
                int n = e.Selection.Count;
                btnExportSelection.Content = n > 1 ? $"Export {n} Images" : "Export Selection";
                btnExportSelection.IsEnabled = n > 0;
            });
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn folder xuất" };
        if (!string.IsNullOrEmpty(txtOutDir.Text)) dlg.InitialDirectory = txtOutDir.Text;
        if (dlg.ShowDialog() == true) txtOutDir.Text = dlg.FolderName;
    }

    private void BtnExportSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _batch == null) return;
        var paths = _workspace.Selection.ToList();
        if (paths.Count == 0)
        {
            MessageBox.Show("Hãy chọn ảnh trước khi export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string format = (cmbFormat.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "png";
        int quality = (int)slQuality.Value;
        int maxLong = int.TryParse(txtMaxLong.Text, out var ml) ? ml : 0;
        string outDir = string.IsNullOrWhiteSpace(txtOutDir.Text)
            ? System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Output")
            : txtOutDir.Text;
        string pattern = string.IsNullOrWhiteSpace(txtPattern.Text) ? "{name}.{ext}" : txtPattern.Text;
        string outputSharpen = (cmbSharpen.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";

        var jobs = paths.Select(p => new BatchJob
        {
            PluginId = ExportBatchAdapter.Plugin,
            OpType = ExportBatchAdapter.OpExport,
            InputPath = p,
            Params = new Dictionary<string, string>
            {
                ["format"] = format,
                ["quality"] = quality.ToString(),
                ["maxLongEdge"] = maxLong.ToString(),
                ["outDir"] = outDir,
                ["pattern"] = pattern,
                ["outputSharpen"] = outputSharpen
            }
        });

        _batch.EnqueueRange(jobs);
    }
}
