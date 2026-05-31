using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageTool.Core;
using ImageTool.Shared;

namespace ImageTool.Host.Workspace;

public partial class ExportPanel : UserControl
{
    private IBatchService? _batch;
    private IWorkspaceService? _workspace;
    private ISettingsService? _settings;
    private bool _loadingPreset;

    public ExportPanel()
    {
        InitializeComponent();
        txtOutDir.Text = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Output");
    }

    public void Bind(IWorkspaceService workspace, IBatchService batch, ISettingsService? settings = null)
    {
        _workspace = workspace;
        _batch = batch;
        _settings = settings;
        _workspace.SelectionChanged += (s, e) =>
            Dispatcher.BeginInvoke(() =>
            {
                int n = e.Selection.Count;
                btnExportSelection.Content = n > 1 ? $"Export {n} Images" : "Export Selection";
                btnExportSelection.IsEnabled = n > 0;
                UpdateEstimate();
            });
        // Cập nhật ước lượng dung lượng khi đổi thông số.
        cmbFormat.SelectionChanged += (_, _) => UpdateEstimate();
        slQuality.ValueChanged += (_, _) => UpdateEstimate();
        txtMaxLong.TextChanged += (_, _) => UpdateEstimate();
        foreach (var chk in new[] { chkSize2048, chkSize1024, chkSize512, chkSize256 })
        { chk.Checked += (_, _) => UpdateEstimate(); chk.Unchecked += (_, _) => UpdateEstimate(); }
        RefreshPresetList();
    }

    /// <summary>Ước lượng dung lượng file xuất cho ảnh active (xấp xỉ, hiển thị nhanh).</summary>
    private void UpdateEstimate()
    {
        if (txtEstSize == null) return;
        var path = _workspace?.ActiveImage;
        if (string.IsNullOrEmpty(path)) { txtEstSize.Text = ""; return; }
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(path);
            if (info == null) { txtEstSize.Text = ""; return; }
            int sw = info.Width, sh = info.Height;
            string format = (cmbFormat.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "png";
            int quality = (int)slQuality.Value;

            var sizes = new System.Collections.Generic.List<int>();
            foreach (var chk in new[] { chkSize2048, chkSize1024, chkSize512, chkSize256 })
                if (chk.IsChecked == true && int.TryParse((string)chk.Tag, out var sz)) sizes.Add(sz);

            if (sizes.Count > 0)
            {
                long total = 0;
                foreach (var sz in sizes) total += ExportSizeEstimator.EstimateBytes(format, sw, sh, sz, quality);
                txtEstSize.Text = $"≈ {ExportSizeEstimator.Format(total)} / ảnh ({sizes.Count} bản)";
            }
            else
            {
                int maxLong = int.TryParse(txtMaxLong.Text, out var ml) ? ml : 0;
                long b = ExportSizeEstimator.EstimateBytes(format, sw, sh, maxLong, quality);
                txtEstSize.Text = $"≈ {ExportSizeEstimator.Format(b)} / ảnh";
            }
        }
        catch { txtEstSize.Text = ""; }
    }

    private void RefreshPresetList()
    {
        if (cmbPreset == null) return;
        _loadingPreset = true;
        cmbPreset.Items.Clear();
        cmbPreset.Items.Add(new ComboBoxItem { Content = "(preset)", Tag = null });
        cmbPreset.SelectedIndex = 0;
        if (_settings != null)
            foreach (var p in _settings.Current.ExportPresets)
                cmbPreset.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
        _loadingPreset = false;
    }

    private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPreset) return;
        if (cmbPreset.SelectedItem is not ComboBoxItem item || item.Tag is not ExportPreset preset) return;
        ApplyPreset(preset);
    }

    private void ApplyPreset(ExportPreset p)
    {
        SelectByTag(cmbFormat, p.Format);
        slQuality.Value = p.Quality;
        txtMaxLong.Text = p.MaxLongEdge.ToString();
        if (!string.IsNullOrEmpty(p.OutDir)) txtOutDir.Text = p.OutDir;
        txtPattern.Text = p.Pattern;
        SelectByTag(cmbSharpen, p.OutputSharpen);
        chkCopyExif.IsChecked = p.CopyExif;
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (var it in combo.Items)
            if (it is ComboBoxItem ci && string.Equals(ci.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            { combo.SelectedItem = ci; return; }
    }

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        var name = PromptName();
        if (string.IsNullOrWhiteSpace(name)) return;
        var preset = new ExportPreset
        {
            Name = name.Trim(),
            Format = (cmbFormat.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "png",
            Quality = (int)slQuality.Value,
            MaxLongEdge = int.TryParse(txtMaxLong.Text, out var ml) ? ml : 0,
            Pattern = string.IsNullOrWhiteSpace(txtPattern.Text) ? "{name}.{ext}" : txtPattern.Text,
            OutputSharpen = (cmbSharpen.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none",
            OutDir = txtOutDir.Text,
            CopyExif = chkCopyExif.IsChecked == true,
        };
        // thay nếu trùng tên.
        _settings.Current.ExportPresets.RemoveAll(x => string.Equals(x.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        _settings.Current.ExportPresets.Add(preset);
        _settings.Save();
        RefreshPresetList();
    }

    private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (cmbPreset.SelectedItem is not ComboBoxItem item || item.Tag is not ExportPreset preset) return;
        _settings.Current.ExportPresets.RemoveAll(x => string.Equals(x.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        RefreshPresetList();
    }

    private static string? PromptName()
    {
        var dlg = new Window
        {
            Title = "Export Preset", Width = 320, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.Black, ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow
        };
        var sp = new StackPanel { Margin = new Thickness(14) };
        sp.Children.Add(new TextBlock { Text = "Tên preset:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 6) });
        var txt = new TextBox { Padding = new Thickness(4), FontSize = 13 };
        sp.Children.Add(txt);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 70, Height = 26, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 70, Height = 26, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = txt.Text; dlg.DialogResult = true; };
        cancel.Click += (_, _) => dlg.DialogResult = false;
        row.Children.Add(ok); row.Children.Add(cancel);
        sp.Children.Add(row);
        dlg.Content = sp;
        dlg.ShowDialog();
        return result;
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
        string copyExif = chkCopyExif.IsChecked == true ? "true" : "false";

        // Xuất đa kích thước: thu thập các size được tick. Rỗng -> dùng maxLong đơn (như cũ).
        var sizes = new System.Collections.Generic.List<int>();
        foreach (var chk in new[] { chkSize2048, chkSize1024, chkSize512, chkSize256 })
            if (chk.IsChecked == true && int.TryParse((string)chk.Tag, out var sz)) sizes.Add(sz);

        var jobs = new System.Collections.Generic.List<BatchJob>();
        foreach (var p in paths)
        {
            if (sizes.Count > 0)
            {
                // 1 job/size; thêm hậu tố _{size} trước đuôi nếu pattern chưa có token để tránh ghi đè.
                foreach (var sz in sizes)
                {
                    string pat = pattern.Contains("{size}") ? pattern : InsertSizeToken(pattern);
                    jobs.Add(MakeJob(p, format, quality, sz, outDir, pat, outputSharpen, copyExif, sz));
                }
            }
            else
            {
                jobs.Add(MakeJob(p, format, quality, maxLong, outDir, pattern, outputSharpen, copyExif, null));
            }
        }

        _batch.EnqueueRange(jobs);
    }

    private static BatchJob MakeJob(string path, string format, int quality, int maxLong,
        string outDir, string pattern, string outputSharpen, string copyExif, int? sizeToken)
    {
        var p = new Dictionary<string, string>
        {
            ["format"] = format,
            ["quality"] = quality.ToString(),
            ["maxLongEdge"] = maxLong.ToString(),
            ["outDir"] = outDir,
            ["pattern"] = pattern,
            ["outputSharpen"] = outputSharpen,
            ["copyExif"] = copyExif,
        };
        if (sizeToken.HasValue) p["sizeToken"] = sizeToken.Value.ToString();
        return new BatchJob
        {
            PluginId = ExportBatchAdapter.Plugin,
            OpType = ExportBatchAdapter.OpExport,
            InputPath = path,
            Params = p,
        };
    }

    // Chèn token {size} vào pattern: "{name}.{ext}" -> "{name}_{size}.{ext}".
    private static string InsertSizeToken(string pattern)
    {
        int dot = pattern.LastIndexOf(".{ext}", StringComparison.OrdinalIgnoreCase);
        if (dot >= 0) return pattern.Insert(dot, "_{size}");
        return pattern + "_{size}";
    }
}
