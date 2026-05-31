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
        cmbFormat.SelectionChanged += (_, _) => { UpdateAdvancedVisibility(); UpdateEstimate(); };
        slQuality.ValueChanged += (_, _) => UpdateEstimate();
        txtMaxLong.TextChanged += (_, _) => UpdateEstimate();
        foreach (var chk in new[] { chkSize2048, chkSize1024, chkSize512, chkSize256 })
        { chk.Checked += (_, _) => UpdateEstimate(); chk.Unchecked += (_, _) => UpdateEstimate(); }
        chkPngPalette.Checked += (_, _) => UpdatePngPaletteVisibility();
        chkPngPalette.Unchecked += (_, _) => UpdatePngPaletteVisibility();
        // Tuỳ chọn nén nâng cao -> cập nhật ước lượng dung lượng.
        txtTargetKB.TextChanged += (_, _) => UpdateEstimate();
        cmbJpegSubsample.SelectionChanged += (_, _) => UpdateEstimate();
        cmbWebpMode.SelectionChanged += (_, _) => UpdateEstimate();
        cmbTiffCompression.SelectionChanged += (_, _) => UpdateEstimate();
        slPngLevel.ValueChanged += (_, _) => UpdateEstimate();
        slPngColors.ValueChanged += (_, _) => UpdateEstimate();
        chkPngPalette.Checked += (_, _) => UpdateEstimate();
        chkPngPalette.Unchecked += (_, _) => UpdateEstimate();
        UpdateAdvancedVisibility();
        UpdatePngPaletteVisibility();
        RefreshPresetList();
    }

    /// <summary>Hiện đúng nhóm tuỳ chọn nén theo định dạng đang chọn (gọn, không rối UI).</summary>
    private void UpdateAdvancedVisibility()
    {
        string fmt = (cmbFormat.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "png";
        Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        bool isJpeg = fmt is "jpg" or "jpeg";
        bool isWebpLossy = fmt == "webp"; // target áp dụng cho webp (lossy) — code-behind chỉ gửi param.
        pnlJpeg.Visibility = V(isJpeg);
        pnlPng.Visibility = V(fmt == "png");
        pnlWebp.Visibility = V(fmt == "webp");
        pnlTiff.Visibility = V(fmt is "tif" or "tiff");
        pnlTargetSize.Visibility = V(isJpeg || isWebpLossy);
    }

    private void UpdatePngPaletteVisibility()
        => pnlPngColors.Visibility = chkPngPalette.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;


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
            var opts = CollectAdvancedParams();

            // Nếu đặt dung lượng mục tiêu -> hiển thị mục tiêu thay vì ước lượng theo quality.
            if (int.TryParse(txtTargetKB.Text, out var tkb) && tkb > 0 && format is "jpg" or "jpeg" or "webp")
            {
                txtEstSize.Text = $"🎯 mục tiêu ≤ {tkb} KB / ảnh";
                return;
            }

            var sizes = new System.Collections.Generic.List<int>();
            foreach (var chk in new[] { chkSize2048, chkSize1024, chkSize512, chkSize256 })
                if (chk.IsChecked == true && int.TryParse((string)chk.Tag, out var sz)) sizes.Add(sz);

            if (sizes.Count > 0)
            {
                long total = 0;
                foreach (var sz in sizes) total += ExportSizeEstimator.EstimateBytesWithOptions(format, sw, sh, sz, quality, opts);
                txtEstSize.Text = $"≈ {ExportSizeEstimator.Format(total)} / ảnh ({sizes.Count} bản)";
            }
            else
            {
                int maxLong = int.TryParse(txtMaxLong.Text, out var ml) ? ml : 0;
                long b = ExportSizeEstimator.EstimateBytesWithOptions(format, sw, sh, maxLong, quality, opts);
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

        // Nén nâng cao.
        txtTargetKB.Text = p.TargetKB.ToString();
        chkStripMeta.IsChecked = p.StripMetadata;
        SelectByTag(cmbJpegSubsample, p.JpegSubsample);
        chkJpegProgressive.IsChecked = p.JpegProgressive;
        slPngLevel.Value = Math.Clamp(p.PngLevel, 0, 9);
        chkPngPalette.IsChecked = p.PngPaletteColors > 0;
        if (p.PngPaletteColors > 0) slPngColors.Value = Math.Clamp(p.PngPaletteColors, 2, 256);
        SelectByTag(cmbWebpMode, p.WebpMode);
        slWebpMethod.Value = Math.Clamp(p.WebpMethod, 0, 6);
        SelectByTag(cmbTiffCompression, p.TiffCompression);
        SelectByTag(cmbOutputProfile, string.IsNullOrEmpty(p.OutputProfile) ? "none" : p.OutputProfile);
        UpdateAdvancedVisibility();
        UpdatePngPaletteVisibility();
        UpdateEstimate();
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
            // Nén nâng cao.
            TargetKB = int.TryParse(txtTargetKB.Text, out var tkb) ? tkb : 0,
            StripMetadata = chkStripMeta.IsChecked == true,
            JpegSubsample = (cmbJpegSubsample.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "420",
            JpegProgressive = chkJpegProgressive.IsChecked == true,
            PngLevel = (int)slPngLevel.Value,
            PngPaletteColors = chkPngPalette.IsChecked == true ? (int)slPngColors.Value : 0,
            WebpMode = (cmbWebpMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "lossy",
            WebpMethod = (int)slWebpMethod.Value,
            TiffCompression = (cmbTiffCompression.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "lzw",
            OutputProfile = (cmbOutputProfile.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none",
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
        var adv = CollectAdvancedParams();

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
                    jobs.Add(MakeJob(p, format, quality, sz, outDir, pat, outputSharpen, copyExif, sz, adv));
                }
            }
            else
            {
                jobs.Add(MakeJob(p, format, quality, maxLong, outDir, pattern, outputSharpen, copyExif, null, adv));
            }
        }

        _batch.EnqueueRange(jobs);
    }

    /// <summary>Thu thập tham số nén nâng cao (Squoosh-style) theo định dạng đang chọn.</summary>
    private Dictionary<string, string> CollectAdvancedParams()
    {
        var d = new Dictionary<string, string>();
        if (int.TryParse(txtTargetKB.Text, out var tkb) && tkb > 0) d["targetKB"] = tkb.ToString();
        if (chkStripMeta.IsChecked == true) d["stripMetadata"] = "true";
        if (!string.IsNullOrWhiteSpace(txtBlindWm.Text)) d["blindWatermark"] = txtBlindWm.Text.Trim();
        string outProf = (cmbOutputProfile.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
        if (outProf != "none") d["outputProfile"] = outProf;

        string fmt = (cmbFormat.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "png";
        switch (fmt)
        {
            case "jpg":
            case "jpeg":
                d["jpegSubsample"] = (cmbJpegSubsample.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "420";
                if (chkJpegProgressive.IsChecked == true) d["jpegProgressive"] = "true";
                break;
            case "png":
                d["pngLevel"] = ((int)slPngLevel.Value).ToString();
                if (chkPngPalette.IsChecked == true)
                {
                    d["pngColorType"] = "palette";
                    d["pngPaletteColors"] = ((int)slPngColors.Value).ToString();
                }
                break;
            case "webp":
                d["webpMode"] = (cmbWebpMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "lossy";
                d["webpMethod"] = ((int)slWebpMethod.Value).ToString();
                break;
            case "tif":
            case "tiff":
                d["tiffCompression"] = (cmbTiffCompression.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "lzw";
                break;
        }
        return d;
    }

    private static BatchJob MakeJob(string path, string format, int quality, int maxLong,
        string outDir, string pattern, string outputSharpen, string copyExif, int? sizeToken,
        Dictionary<string, string>? advanced = null)
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
        if (advanced != null)
            foreach (var kv in advanced) p[kv.Key] = kv.Value;
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

    private void BtnContactSheet_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null) return;
        var paths = _workspace.Selection.ToList();
        if (paths.Count < 2)
        {
            MessageBox.Show("Chọn ít nhất 2 ảnh để tạo contact sheet.", "Contact Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string outDir = string.IsNullOrWhiteSpace(txtOutDir.Text)
            ? System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Output")
            : txtOutDir.Text;
        string outPath = FileNameTokenizer.EnsureUniquePath(System.IO.Path.Combine(outDir, "contact_sheet.jpg"));

        // Cột theo số ảnh: ~căn bậc 2, tối thiểu 3, tối đa 6.
        int cols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(paths.Count)), 3, 6);
        var opt = new ContactSheet.Options { Columns = cols, SheetWidth = 2000, ShowFileName = true };

        try
        {
            int drawn = ContactSheet.Render(paths, outPath, opt);
            MessageBox.Show(drawn > 0 ? $"Đã tạo contact sheet ({drawn} ảnh):\n{outPath}" : "Không ghép được ảnh nào.",
                "Contact Sheet", MessageBoxButton.OK, drawn > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ImageTool.Shared.AppLog.Error("ExportPanel.ContactSheet", outPath, ex);
            MessageBox.Show("Lỗi tạo contact sheet (xem app.log).", "Contact Sheet", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
