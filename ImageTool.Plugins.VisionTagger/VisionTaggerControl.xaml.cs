using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ImageTool.Core;
using Microsoft.Win32;

namespace ImageTool.Plugins.VisionTagger;

public partial class VisionTaggerControl : UserControl
{
    public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string>();

    private IWorkspaceService? _workspace;
    private IModelDownloader? _downloader;
    private IImageToolHost? _host;
    private IImageMetaService? _meta;
    private ISettingsService? _settings;
    private WdTaggerProcessor? _processor;
    private string? _currentPath;

    public VisionTaggerControl()
    {
        InitializeComponent();
        lstTags.ItemsSource = Tags;
    }

    public void AttachServices(IServiceProvider sp)
    {
        _workspace = sp.GetService(typeof(IWorkspaceService)) as IWorkspaceService;
        _downloader = sp.GetService(typeof(IModelDownloader)) as IModelDownloader;
        _host = sp.GetService(typeof(IImageToolHost)) as IImageToolHost;
        _meta = sp.GetService(typeof(IImageMetaService)) as IImageMetaService;
        _settings = sp.GetService(typeof(ISettingsService)) as ISettingsService;

        if (_host != null)
        {
            _host.ActiveImageChanged += (s, path) => Dispatcher.BeginInvoke(() => OnActiveImageChanged(path));
            OnActiveImageChanged(_host.ActiveImagePath);
        }
    }

    private void OnActiveImageChanged(string? path)
    {
        _currentPath = path;
        bool has = !string.IsNullOrEmpty(path) && File.Exists(path);
        txtActiveImage.Text = has ? Path.GetFileName(path) : "(chưa chọn ảnh)";
        btnAnalyze.IsEnabled = has;
        txtDescription.Text = "";
        Tags.Clear();
    }

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentPath) || !File.Exists(_currentPath))
        {
            MessageBox.Show("Hãy chọn ảnh trước.", "Auto Tagger", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_downloader == null)
        {
            MessageBox.Show("Service chưa khởi tạo.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        btnAnalyze.IsEnabled = false;
        pnlLoading.Visibility = Visibility.Visible;
        txtDescription.Text = "";
        Tags.Clear();

        try
        {
            // Bước 1: Đảm bảo model + tags csv tồn tại (auto download)
            txtDescription.Text = "Đang chuẩn bị model (lần đầu sẽ tải ~380MB)...";
            var modelPath = await _downloader.EnsureAsync(KnownModels.WdViTV3, new Progress<DownloadProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                    txtDescription.Text = $"Đang tải model: {p.BytesReceived / (1024.0 * 1024):N1} MB ({p.Percent:N1}%)");
            }));
            var tagsPath = await _downloader.EnsureAsync(KnownModels.WdViTV3Tags);

            // Bước 2: Lazy init processor
            _processor ??= await Task.Run(() => new WdTaggerProcessor(modelPath, tagsPath));

            // Bước 3: Inference
            txtDescription.Text = "Đang phân tích bằng AI...";
            var result = await Task.Run(() => _processor.Run(_currentPath, generalThreshold: 0.35f, characterThreshold: 0.85f));

            // Bước 4: Build description + tags display
            var topRating = result.Rating.FirstOrDefault();
            var topGeneral = result.General.Take(8).Select(x => x.Tag).ToList();
            var characterTags = result.Character.Select(x => x.Tag).ToList();

            txtDescription.Text = (topRating.Tag != null ? $"[{topRating.Tag}] " : "")
                + string.Join(", ", topGeneral);

            Tags.Clear();
            foreach (var t in characterTags) Tags.Add($"★ {t}");
            foreach (var (tag, score) in result.General.Take(60))
                Tags.Add(tag);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi: {ex.Message}", "Auto Tagger", MessageBoxButton.OK, MessageBoxImage.Error);
            txtDescription.Text = "";
        }
        finally
        {
            pnlLoading.Visibility = Visibility.Collapsed;
            btnAnalyze.IsEnabled = true;
        }
    }

    private void BtnCopyDesc_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(txtDescription.Text)) Clipboard.SetText(txtDescription.Text);
    }

    private void BtnCopyTags_Click(object sender, RoutedEventArgs e)
    {
        if (Tags.Count == 0) return;
        Clipboard.SetText(string.Join(", ", Tags));
    }

    /// <summary>Lưu các tag AI vào metadata ảnh (gộp với keyword sẵn có), phục vụ tìm kiếm/smart collection.</summary>
    private void BtnSaveTags_Click(object sender, RoutedEventArgs e)
    {
        if (_meta == null || string.IsNullOrEmpty(_currentPath))
        {
            MessageBox.Show("Chưa có ảnh hoặc service metadata.", "Lưu Keywords", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (Tags.Count == 0) return;

        // Bỏ tiền tố "★ " của character tag, gộp với keyword đang có, khử trùng (không phân biệt hoa thường).
        var existing = _meta.Get(_currentPath).Tags;
        var merged = new List<string>(existing);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Tags)
        {
            var t = raw.StartsWith("★ ") ? raw.Substring(2).Trim() : raw.Trim();
            if (t.Length > 0 && seen.Add(t)) merged.Add(t);
        }
        _meta.SetTags(_currentPath, merged);
        _settings?.AddRecentTags(merged);
        MessageBox.Show($"Đã lưu {merged.Count} keyword vào ảnh.", "Lưu Keywords", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>AI batch tagging (#3): chạy tagger cho mọi ảnh đang chọn, lưu keyword (gộp, khử trùng).</summary>
    private async void BtnTagSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _meta == null || _downloader == null)
        {
            MessageBox.Show("Service chưa khởi tạo.", "Batch Tag", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var targets = _workspace.Selection.Where(File.Exists).ToList();
        if (targets.Count == 0 && !string.IsNullOrEmpty(_currentPath) && File.Exists(_currentPath))
            targets.Add(_currentPath);
        if (targets.Count == 0)
        {
            MessageBox.Show("Hãy chọn ít nhất 1 ảnh.", "Batch Tag", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        btnTagSelection.IsEnabled = false;
        btnAnalyze.IsEnabled = false;
        pnlLoading.Visibility = Visibility.Visible;
        int done = 0, ok = 0;
        try
        {
            var modelPath = await _downloader.EnsureAsync(KnownModels.WdViTV3, new Progress<DownloadProgress>(p =>
                Dispatcher.BeginInvoke(() => txtDescription.Text = $"Tải model: {p.Percent:N0}%")));
            var tagsPath = await _downloader.EnsureAsync(KnownModels.WdViTV3Tags);
            _processor ??= await Task.Run(() => new WdTaggerProcessor(modelPath, tagsPath));

            foreach (var path in targets)
            {
                _ = Dispatcher.BeginInvoke(() => txtDescription.Text = $"Đang tag {done + 1}/{targets.Count}: {Path.GetFileName(path)}");
                try
                {
                    var result = await Task.Run(() => _processor!.Run(path, generalThreshold: 0.35f, characterThreshold: 0.85f));
                    var tags = result.General.Take(30).Select(x => x.Tag)
                        .Concat(result.Character.Select(x => x.Tag)).ToList();
                    if (tags.Count > 0)
                    {
                        var existing = _meta.Get(path).Tags;
                        var merged = new List<string>(existing);
                        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
                        foreach (var t in tags) if (t.Length > 0 && seen.Add(t)) merged.Add(t);
                        _meta.SetTags(path, merged);
                        ok++;
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Batch tag lỗi {path}: {ex.Message}"); }
                done++;
            }
            MessageBox.Show($"Đã tag {ok}/{targets.Count} ảnh (lưu vào keyword).", "Batch Tag", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi batch tag: {ex.Message}", "Batch Tag", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            pnlLoading.Visibility = Visibility.Collapsed;
            btnTagSelection.IsEnabled = true;
            btnAnalyze.IsEnabled = !string.IsNullOrEmpty(_currentPath);
            txtDescription.Text = "";
        }
    }
}
