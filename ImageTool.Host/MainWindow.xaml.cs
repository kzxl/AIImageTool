using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageTool.Core;
using ImageTool.Host.Workspace;
using ImageTool.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ImageTool.Host;

public partial class MainWindow : Window
{
    private readonly PluginLoader _pluginLoader;
    private readonly IServiceProvider _serviceProvider;
    private readonly AiWorkerManager _aiManager;
    private readonly IWorkspaceService _workspace;
    private readonly IThumbnailService _thumbs;
    private readonly IImageMetaService _meta;
    private readonly IHistoryService _history;
    private readonly IBatchService _batch;
    private readonly ISettingsService _settings;
    private readonly IStyleService _styles;
    private readonly ImageToolHostProvider _hostProvider;
    private readonly DevelopClipboard _developClipboard;
    private ImageTool.Shared.FolderWatcher? _folderWatcher;
    private readonly AiMaskService _aiMaskService;
    private List<PluginEntry> _pluginEntries = new();
    private ToolsWindow? _toolsWindow;
    private Grid? _toolsHostOriginalParent;
    private int _toolsHostOriginalColumn;
    private bool _suppressModeSync;

    public MainWindow(
        PluginLoader pluginLoader,
        IServiceProvider serviceProvider,
        AiWorkerManager aiManager,
        IWorkspaceService workspace,
        IThumbnailService thumbs,
        IImageMetaService meta,
        IHistoryService history,
        IBatchService batch,
        ISettingsService settings,
        IStyleService styles,
        ImageToolHostProvider hostProvider)
    {
        InitializeComponent();
        _pluginLoader = pluginLoader;
        _serviceProvider = serviceProvider;
        _aiManager = aiManager;
        _workspace = workspace;
        _thumbs = thumbs;
        _meta = meta;
        _history = history;
        _batch = batch;
        _settings = settings;
        _styles = styles;
        _hostProvider = hostProvider;
        _hostProvider.Host = centerView; // CenterPreview implement IImageToolHost
        _developClipboard = serviceProvider.GetRequiredService<DevelopClipboard>();
        _aiMaskService = serviceProvider.GetRequiredService<AiMaskService>(); // đăng ký delegate AI denoise + segmentation

        // Load saved batch parallel
        _batch.MaxParallel = Math.Max(1, _settings.Current.BatchParallel);

        browser.Bind(_workspace, _thumbs, _meta);
        browser.BindCollections(_serviceProvider.GetRequiredService<ICatalogService>(), _workspace);
        browser.BindContext(_history, _developClipboard);
        centerView.Bind(_workspace, _thumbs, _meta, _history);
        centerView.BindContext(_developClipboard);
        centerView.ProgressReported += (s, p) =>
            Dispatcher.BeginInvoke(() => txtStatus.Text = p.Percent < 0 ? "Ready" : $"{p.Status} {p.Percent}%");
        filmstrip.Bind(_workspace, _thumbs, _meta);
        filmstrip.BindContext(_history, _developClipboard);
        infoPanel.Bind(_workspace, _meta, _settings);
        historyPanel.Bind(_workspace, _history, centerView.Renderer);
        batchPanel.Bind(_batch);
        exportPanel.Bind(_workspace, _batch, _settings);
        stylePanel.Bind(_styles, _workspace, _batch);
        stylePanel.StyleHoverChanged += StylePanel_StyleHoverChanged;
        developPanel.Bind(_workspace, _history, centerView.Renderer, _developClipboard, _styles,
            serviceProvider.GetRequiredService<LensfunService>());
        developPanel.BindActiveLayersHost(panelActiveLayers);
        developPanel.RequestClippingPreview += (s, active) =>
        {
            Dispatcher.BeginInvoke(() => centerView.SetTemporaryClipOverlay(active));
        };
        centerView.BindCropPanel(developPanel);
        centerView.BindBrushPanel(developPanel);
        centerView.BindWhiteBalancePick(developPanel);
        centerView.BindHealingPanel(developPanel);
        centerView.BindLiquifyPanel(developPanel);
        centerView.BindTat(developPanel);

        // AI Subject mask: DevelopPanel yêu cầu -> AiMaskService sinh mask PNG -> AddRasterMask.
        developPanel.SubjectMaskRequested += async (s, path) =>
        {
            txtStatus.Text = "AI: đang phân vùng chủ thể (lần đầu sẽ tải model)...";
            try
            {
                var maskPath = await _aiMaskService.GenerateSubjectMaskAsync(path);
                if (maskPath != null)
                {
                    developPanel.AddRasterMask(maskPath);
                    txtStatus.Text = "AI: đã tạo mask chủ thể.";
                }
                else txtStatus.Text = "AI: không tạo được mask (xem app.log).";
            }
            catch (Exception ex)
            {
                ImageTool.Shared.AppLog.Error("MainWindow.SubjectMask", path, ex);
                txtStatus.Text = "AI: lỗi tạo mask.";
            }
        };

        _aiManager.StartWorker();
        RestorePanelLayout();
        Closed += (s, e) =>
        {
            SavePanelLayout();
            _aiManager.Dispose();
            _aiMaskService.Dispose();
            (_thumbs as IDisposable)?.Dispose();
        };

        _workspace.FolderOpened += (s, e) =>
            Dispatcher.BeginInvoke(() => BuildBreadcrumb(e.FolderPath));

        _workspace.SelectionChanged += (s, e) =>
            Dispatcher.BeginInvoke(() => UpdateSelectionInfo(e.Selection.Count));

        centerView.ModeChanged += (s, mode) =>
        {
            if (_suppressModeSync) return;
            _suppressModeSync = true;
            switch (mode)
            {
                case LighttableMode.Single: rbModeSingle.IsChecked = true; break;
                case LighttableMode.Grid:   rbModeGrid.IsChecked = true; break;
                case LighttableMode.Cull:   rbModeCull.IsChecked = true; break;
                case LighttableMode.Full:   rbModeFull.IsChecked = true; break;
            }
            _suppressModeSync = false;
        };

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Toast: tự ẩn sau timer.
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            // Fade-out animation
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                new Duration(TimeSpan.FromMilliseconds(300)));
            fadeOut.Completed += (_, _) => toastBorder.Visibility = Visibility.Collapsed;
            toastBorder.BeginAnimation(OpacityProperty, fadeOut);
        };
        // Báo khi job batch xong (export/style/upscale...).
        _batch.JobUpdated += (s, job) =>
        {
            if (job.Status == BatchJobStatus.Completed)
                Dispatcher.BeginInvoke(() => ShowToast($"Xong: {job.DisplayName}"));
            else if (job.Status == BatchJobStatus.Failed)
                Dispatcher.BeginInvoke(() => ShowToast($"Lỗi: {job.DisplayName}"));
        };

        // Track recent images when user makes edits
        _history.HistoryChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.ImagePath) && _history.GetPointer(e.ImagePath) > 0)
                _settings.AddRecentImage(e.ImagePath);
        };

        LoadPlugins();
    }

    private void BuildBreadcrumb(string folderPath)
    {
        breadcrumb.Items.Clear();
        if (string.IsNullOrEmpty(folderPath)) return;

        var parts = new List<(string Display, string FullPath)>();
        var p = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!string.IsNullOrEmpty(p))
        {
            string name = Path.GetFileName(p);
            if (string.IsNullOrEmpty(name)) name = p; // Drive root
            parts.Insert(0, (name, p));
            var parent = Path.GetDirectoryName(p);
            if (parent == p) break;
            p = parent ?? string.Empty;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            var (display, full) = parts[i];
            var btn = new Button
            {
                Content = display,
                Style = (System.Windows.Style)FindResource("CrumbButtonStyle"),
                Tag = full
            };
            btn.Click += (s, e) =>
            {
                var path = (string)((Button)s!).Tag;
                if (Directory.Exists(path))
                {
                    _workspace.OpenFolder(path);
                    _settings.AddRecentFolder(path);
                }
            };
            breadcrumb.Items.Add(btn);

            if (i < parts.Count - 1)
            {
                breadcrumb.Items.Add(new TextBlock
                {
                    Text = "›",
                    Foreground = (Brush)FindResource("TextDimBrush"),
                    Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }
    }

    private void UpdateSelectionInfo(int count)
    {
        txtSelInfo.Text = count > 0 ? $"{count} selected" : "";
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        bool ctrlMod = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        bool typingNow = System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox
            || System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

        // Bảng phím tắt: F1 hoặc ? (Shift+/) bật/tắt; Esc đóng. Hoạt động cả khi chưa chọn ảnh.
        if (!typingNow && (e.Key == System.Windows.Input.Key.F1
            || (e.Key == System.Windows.Input.Key.OemQuestion && !ctrlMod)))
        {
            ToggleShortcutOverlay();
            e.Handled = true;
            return;
        }
        if (shortcutOverlay.Visibility == Visibility.Visible && e.Key == System.Windows.Input.Key.Escape)
        {
            shortcutOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        // Điều hướng ảnh bằng ← → hoạt động cả khi chưa có ảnh active (miễn là có ảnh trong danh sách),
        // và bất kể focus đang ở Grid/Filmstrip/Browser (PreviewKeyDown tunneling).
        if (!typingNow && !ctrlMod && _workspace.Images.Count > 0)
        {
            if (e.Key == System.Windows.Input.Key.Left) { NavigateActiveImage(-1); e.Handled = true; return; }
            if (e.Key == System.Windows.Input.Key.Right) { NavigateActiveImage(+1); e.Handled = true; return; }
        }

        if (_workspace.ActiveImage == null) return;
        var path = _workspace.ActiveImage;
        bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;

        // Không nuốt phím khi đang gõ vào ô nhập liệu (TextBox/ComboBox editable).
        bool typing = System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox
            || System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

        // Phím chuyển module kiểu Lightroom (không khi đang gõ, không khi giữ modifier).
        if (!typing && !ctrl && !shift)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.D: SelectRightTab("Develop"); e.Handled = true; return;
                case System.Windows.Input.Key.M: SelectRightTab("Develop"); developPanel.FocusMasking(); e.Handled = true; return;
            }
        }

        if (ctrl && shift)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.C: CopyDevelopSettings(); e.Handled = true; return;
                case System.Windows.Input.Key.V: PasteDevelopSettings(); e.Handled = true; return;
                case System.Windows.Input.Key.E: exportPanel.QuickExport(); e.Handled = true; return;
            }
        }

        if (ctrl && !shift)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.C: if (!typing) { CopyDevelopSettings(); e.Handled = true; return; } break;
                case System.Windows.Input.Key.V: if (!typing) { PasteDevelopSettings(); e.Handled = true; return; } break;
            }
        }

        if (ctrl)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Z:
                {
                    var op = _history.Undo(path);
                    txtStatus.Text = op != null ? $"Hoàn tác: {OpLabel(op)}" : "Không còn gì để hoàn tác";
                    e.Handled = true; return;
                }
                case System.Windows.Input.Key.Y:
                {
                    var op = _history.Redo(path);
                    txtStatus.Text = op != null ? $"Làm lại: {OpLabel(op)}" : "Không còn gì để làm lại";
                    e.Handled = true; return;
                }
            }
        }

        switch (e.Key)
        {
            case System.Windows.Input.Key.D0: ApplyRatingToTargets(0); e.Handled = true; break;
            case System.Windows.Input.Key.D1: ApplyRatingToTargets(1); e.Handled = true; break;
            case System.Windows.Input.Key.D2: ApplyRatingToTargets(2); e.Handled = true; break;
            case System.Windows.Input.Key.D3: ApplyRatingToTargets(3); e.Handled = true; break;
            case System.Windows.Input.Key.D4: ApplyRatingToTargets(4); e.Handled = true; break;
            case System.Windows.Input.Key.D5: ApplyRatingToTargets(5); e.Handled = true; break;
            case System.Windows.Input.Key.P: ApplyPickToTargets(PickFlag.Pick); e.Handled = true; break;
            case System.Windows.Input.Key.X: ApplyPickToTargets(PickFlag.Reject); e.Handled = true; break;
            case System.Windows.Input.Key.U: ApplyPickToTargets(PickFlag.None); e.Handled = true; break;
            case System.Windows.Input.Key.D6: ApplyLabelToTargets(ColorLabel.Red); e.Handled = true; break;
            case System.Windows.Input.Key.D7: ApplyLabelToTargets(ColorLabel.Yellow); e.Handled = true; break;
            case System.Windows.Input.Key.D8: ApplyLabelToTargets(ColorLabel.Green); e.Handled = true; break;
            case System.Windows.Input.Key.D9: ApplyLabelToTargets(ColorLabel.Blue); e.Handled = true; break;
            case System.Windows.Input.Key.B: if (!typing) { ToggleQuickCollection(); e.Handled = true; } break;
            // (← → điều hướng ảnh đã xử lý sớm phía trên, trước guard ActiveImage.)
        }
    }

    private void ToggleShortcutOverlay()
        => shortcutOverlay.Visibility = shortcutOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void ShortcutOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => shortcutOverlay.Visibility = Visibility.Collapsed;

    /// <summary>Chuyển ảnh active sang ảnh kế tiếp (+1) / trước đó (-1) trong danh sách hiện tại.</summary>
    private void NavigateActiveImage(int delta)
    {
        var images = _workspace.Images;
        if (images.Count == 0) return;
        int idx = _workspace.ActiveImage != null ? images.IndexOf(_workspace.ActiveImage) : -1;
        int next = idx < 0 ? (delta > 0 ? 0 : images.Count - 1) : idx + delta;
        next = Math.Clamp(next, 0, images.Count - 1);
        if (next == idx) return;
        var path = images[next];
        _workspace.SetActiveImage(path);
        _workspace.SetSelection(new[] { path });
    }

    /// <summary>Ảnh đích cho thao tác meta nhanh: toàn bộ selection, nếu rỗng thì ảnh active.</summary>
    private List<string> MetaTargets()
        => _workspace.Selection.Count > 0
            ? _workspace.Selection.ToList()
            : (_workspace.ActiveImage != null ? new List<string> { _workspace.ActiveImage } : new List<string>());

    private async void ToggleQuickCollection()
    {
        var active = _workspace.ActiveImage;
        if (string.IsNullOrEmpty(active)) return;
        
        var catalog = _serviceProvider.GetRequiredService<ICatalogService>();
        
        if (!catalog.IsImported(active))
        {
            try
            {
                txtStatus.Text = "Đang import ảnh vào thư viện...";
                await catalog.ImportAsync(new[] { active }, new ImportOptions { Mode = ImportMode.AddInPlace });
                txtStatus.Text = "Đã import ảnh.";
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainWindow.QuickCollection", $"Không thể tự động import ảnh {active}: {ex.Message}");
                ShowToast("Không thể import ảnh này vào thư viện.");
                return;
            }
        }

        var collections = catalog.GetCollections();
        var quickCol = collections.FirstOrDefault(c => string.Equals(c.Name, "Quick Collection", StringComparison.OrdinalIgnoreCase));
        if (quickCol == null)
        {
            quickCol = catalog.CreateCollection("Quick Collection");
        }

        var images = catalog.GetCollectionImages(quickCol.Id);
        bool exists = images.Any(img => string.Equals(img.FilePath, active, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            catalog.RemoveFromCollection(quickCol.Id, new[] { active });
            ShowToast("Đã xoá khỏi Quick Collection");
        }
        else
        {
            catalog.AddToCollection(quickCol.Id, new[] { active });
            ShowToast("Đã thêm vào Quick Collection");
        }
    }

    private void ApplyRatingToTargets(int rating)
    {
        var t = MetaTargets();
        if (t.Count == 0) return;
        _meta.SetRatingMany(t, rating);
        if (t.Count > 1) txtStatus.Text = $"Đặt {rating}★ cho {t.Count} ảnh";
    }

    private void ApplyPickToTargets(PickFlag pick)
    {
        var t = MetaTargets();
        if (t.Count == 0) return;
        _meta.SetPickMany(t, pick);
        if (t.Count > 1)
        {
            string label = pick == PickFlag.Pick ? "Pick" : pick == PickFlag.Reject ? "Reject" : "bỏ cờ";
            txtStatus.Text = $"Đặt {label} cho {t.Count} ảnh";
        }
    }

    private void ApplyLabelToTargets(ColorLabel label)
    {
        var t = MetaTargets();
        if (t.Count == 0) return;
        _meta.SetLabelMany(t, label);
        if (t.Count > 1) txtStatus.Text = $"Gắn nhãn {label} cho {t.Count} ảnh";
    }

    // ===== Copy/Paste Develop settings =====
    private static string OpLabel(EditOperation op)
        => ImageTool.Shared.OpDisplayNames.Get(op.OpType, op.Title);

    /// <summary>Chọn tab panel phải theo header (kiểu LR module switch D/M).</summary>
    private void SelectRightTab(string header)
    {
        foreach (var item in rightTabs.Items)
            if (item is TabItem ti && ti.Header is string h && h == header)
            {
                ti.IsSelected = true;
                return;
            }
    }

    private readonly System.Windows.Threading.DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>Hiển thị toast không chặn ở đáy cửa sổ, tự ẩn sau 3s.</summary>
    public void ShowToast(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            txtToast.Text = message;
            toastBorder.Opacity = 0;
            toastBorder.Visibility = Visibility.Visible;
            // Fade-in animation
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromMilliseconds(200)));
            toastBorder.BeginAnimation(OpacityProperty, fadeIn);
            _toastTimer.Stop();
            _toastTimer.Start();
        });
    }

    private void StylePanel_StyleHoverChanged(object? sender, StyleHoverEventArgs e)
    {
        if (e.StyleId == null)
        {
            centerView.SetTemporaryOperations(null);
        }
        else
        {
            var active = _workspace.ActiveImage;
            if (string.IsNullOrEmpty(active)) return;

            var style = _styles.Styles.FirstOrDefault(s => s.Id == e.StyleId);
            if (style == null) return;

            const string developPlugin = "Develop";
            var styleDev = style.Operations
                .Where(o => string.Equals(o.PluginId, developPlugin, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var targetDev = _history.GetStack(active)
                .Take(_history.GetPointer(active))
                .Where(o => string.Equals(o.PluginId, developPlugin, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var merged = DevelopModules.ApplyStyle(targetDev, styleDev, e.Append)
                .Select(o => new EditOperation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PluginId = o.PluginId,
                    OpType = o.OpType,
                    Title = o.Title,
                    Timestamp = DateTime.UtcNow,
                    Params = new Dictionary<string, string>(o.Params)
                })
                .ToList();

            centerView.SetTemporaryOperations(merged);
        }
    }

    public void CopyDevelopSettings()
    {
        var src = _workspace.ActiveImage;
        if (string.IsNullOrEmpty(src)) return;
        if (_developClipboard.Copy(_history, src))
            txtStatus.Text = _developClipboard.HasData
                ? $"Đã copy settings ({_developClipboard.Count} bước) từ {Path.GetFileName(src)}"
                : "Đã copy (ảnh gốc, không có chỉnh sửa)";
    }

    public void PasteDevelopSettings()
    {
        if (!_developClipboard.HasCopied)
        {
            txtStatus.Text = "Chưa có settings nào được copy (Ctrl+Shift+C trước)";
            return;
        }
        // Áp cho toàn bộ ảnh đang chọn; nếu không có selection thì ảnh active.
        var targets = _workspace.Selection.Count > 0
            ? _workspace.Selection.ToList()
            : (_workspace.ActiveImage != null ? new List<string> { _workspace.ActiveImage } : new List<string>());
        if (targets.Count == 0) return;
        int n = _developClipboard.PasteToMany(_history, targets);
        txtStatus.Text = $"Đã dán settings vào {n} ảnh";
    }

    private void LoadPlugins()
    {
        string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        var plugins = _pluginLoader.LoadPlugins(pluginsPath).ToList();

        var failures = new List<string>(_pluginLoader.LoadErrors);

        foreach (var plugin in plugins)
        {
            try { plugin.Initialize(_serviceProvider); }
            catch (Exception ex)
            {
                failures.Add($"{plugin.Name}: khởi tạo lỗi - {ex.Message}");
                AppLog.Error("MainWindow.LoadPlugins", $"Initialize plugin '{plugin.Name}' lỗi", ex);
            }
        }

        _pluginEntries = plugins
            .Select(p => new PluginEntry(p))
            .ToList();

        lstPlugins.ItemsSource = _pluginEntries;
        if (_pluginEntries.Count > 0) lstPlugins.SelectedIndex = 0;

        if (failures.Count > 0)
            ShowToast(failures.Count == 1
                ? $"Plugin lỗi: {failures[0]}"
                : $"{failures.Count} plugin nạp lỗi (xem app.log)");
    }

    private void LstPlugins_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstPlugins.SelectedItem is PluginEntry entry)
        {
            try { contentPresenter.Content = entry.Plugin.GetUIComponent(); }
            catch (Exception ex)
            {
                contentPresenter.Content = null;
                AppLog.Error("MainWindow.PluginUI", $"GetUIComponent '{entry.Name}' lỗi", ex);
                ShowToast($"Không mở được giao diện plugin '{entry.Name}'");
            }
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục ảnh" };
        if (!string.IsNullOrEmpty(_settings.Current.LastFolder))
            dlg.InitialDirectory = _settings.Current.LastFolder;
        if (dlg.ShowDialog() == true)
        {
            _workspace.OpenFolder(dlg.FolderName);
            _settings.AddRecentFolder(dlg.FolderName);
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var catalog = _serviceProvider.GetRequiredService<ICatalogService>();
        var dlg = new ImportDialog(catalog, _thumbs, _workspace) { Owner = this };
        dlg.ShowDialog();
    }

    /// <summary>Menu công cụ tổng (Merge HDR / Focus Stack / Batch Rename) trên selection — đưa tính
    /// năng vốn ẩn trong context menu ra toolbar cho dễ tìm.</summary>
    private void BtnMore_Click(object sender, RoutedEventArgs e)
    {
        var targets = MetaTargets(); // selection, hoặc ảnh active nếu selection rỗng
        var menu = new System.Windows.Controls.ContextMenu();

        var miHdr = new System.Windows.Controls.MenuItem { Header = "Merge to HDR (Exposure Fusion)", IsEnabled = targets.Count >= 2 };
        miHdr.Click += (_, _) => ImageContextMenu.RunMerge(targets, ImageTool.Shared.MergeService.Mode.Hdr);
        menu.Items.Add(miHdr);

        var miFocus = new System.Windows.Controls.MenuItem { Header = "Focus Stack (nét toàn bộ)", IsEnabled = targets.Count >= 2 };
        miFocus.Click += (_, _) => ImageContextMenu.RunMerge(targets, ImageTool.Shared.MergeService.Mode.FocusStack);
        menu.Items.Add(miFocus);

        var miPano = new System.Windows.Controls.MenuItem { Header = "Panorama (ghép ảnh chồng lấn)", IsEnabled = targets.Count >= 2 };
        miPano.Click += (_, _) => ImageContextMenu.RunMerge(targets, ImageTool.Shared.MergeService.Mode.Panorama);
        menu.Items.Add(miPano);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var miRename = new System.Windows.Controls.MenuItem { Header = "Batch Rename...", IsEnabled = targets.Count >= 1 };
        miRename.Click += (_, _) => ImageContextMenu.RunBatchRename(targets);
        menu.Items.Add(miRename);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var miDup = new System.Windows.Controls.MenuItem { Header = "Tìm ảnh trùng/gần trùng", IsEnabled = _workspace.Images.Count >= 2 };
        miDup.Click += (_, _) => FindDuplicates();
        menu.Items.Add(miDup);

        var miUrl = new System.Windows.Controls.MenuItem { Header = "Import ảnh từ URL..." };
        miUrl.Click += (_, _) => ImportFromUrl();
        menu.Items.Add(miUrl);

        var miWatch = new System.Windows.Controls.MenuItem
        {
            Header = _folderWatcher?.IsWatching == true ? "Tắt theo dõi thư mục" : "Theo dõi thư mục (auto-import)",
            IsEnabled = !string.IsNullOrEmpty(_workspace.CurrentFolder),
        };
        miWatch.Click += (_, _) => ToggleWatchFolder();
        menu.Items.Add(miWatch);

        if (targets.Count == 0)
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "(chọn ảnh trước)", IsEnabled = false });

        menu.PlacementTarget = btnMore;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Tìm ảnh gần trùng (#1) trong danh sách hiện tại, chọn toàn bộ nhóm trùng để xem/cull.</summary>
    private async void FindDuplicates()
    {
        var paths = _workspace.Images.ToList();
        if (paths.Count < 2) return;
        txtStatus.Text = "Đang quét ảnh trùng...";
        try
        {
            var decoders = ImageTool.Imaging.ImageDecoderRegistry.CreateDefault();
            var groups = await System.Threading.Tasks.Task.Run(
                () => ImageTool.Shared.DuplicateFinder.FindGroups(paths, decoders, 10));

            if (groups.Count == 0)
            {
                txtStatus.Text = "";
                MessageBox.Show("Không tìm thấy ảnh trùng/gần trùng.", "Tìm ảnh trùng",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Chọn toàn bộ ảnh thuộc các nhóm trùng -> user xem/lọc/xoá.
            var selection = new List<string>();
            int dupCount = 0;
            foreach (var g in groups) { selection.AddRange(g); dupCount += g.Count - 1; }
            _workspace.SetSelection(selection);
            if (selection.Count > 0) _workspace.SetActiveImage(selection[0]);
            txtStatus.Text = $"Tìm thấy {groups.Count} nhóm, {dupCount} ảnh trùng (đã chọn {selection.Count}).";
        }
        catch (Exception ex)
        {
            txtStatus.Text = "";
            AppLog.Error("MainWindow.FindDuplicates", "quét trùng lỗi", ex);
            MessageBox.Show($"Lỗi quét ảnh trùng: {ex.Message}", "Tìm ảnh trùng",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static System.Net.Http.HttpClient? _urlHttp;

    /// <summary>Import ảnh từ URL (dán link): tải an toàn về thư mục hiện tại rồi chọn ảnh.</summary>
    private async void ImportFromUrl()
    {
        var dlg = new Workspace.InputDialog("Import từ URL", "Dán link ảnh (http/https):", "https://");
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        string url = dlg.Result.Trim();

        if (!ImageTool.Shared.UrlImageImporter.IsValidImageUrl(url, out _))
        {
            MessageBox.Show("URL không hợp lệ (chỉ chấp nhận http/https).", "Import từ URL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string destFolder = !string.IsNullOrEmpty(_workspace.CurrentFolder) && System.IO.Directory.Exists(_workspace.CurrentFolder)
            ? _workspace.CurrentFolder!
            : System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Downloads");

        txtStatus.Text = "Đang tải ảnh từ URL...";
        try
        {
            _urlHttp ??= new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            string saved = await ImageTool.Shared.UrlImageImporter.DownloadAsync(url, destFolder, _urlHttp);
            txtStatus.Text = $"Đã tải: {System.IO.Path.GetFileName(saved)}";

            // Nếu đang ở đúng thư mục tải về -> refresh + chọn ảnh mới; nếu không -> mở thư mục đó.
            if (string.Equals(_workspace.CurrentFolder, destFolder, StringComparison.OrdinalIgnoreCase))
                _workspace.OpenFolder(destFolder);
            else
                _workspace.OpenFolder(destFolder);
            _workspace.SetActiveImage(saved);
            _workspace.SetSelection(new[] { saved });
        }
        catch (Exception ex)
        {
            txtStatus.Text = "";
            AppLog.Warn("MainWindow.ImportFromUrl", $"{url}: {ex.Message}");
            MessageBox.Show($"Không tải được ảnh: {ex.Message}", "Import từ URL",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Bật/tắt theo dõi thư mục hiện tại (#2): ảnh mới copy vào sẽ tự refresh + chọn.</summary>
    private void ToggleWatchFolder()
    {
        if (_folderWatcher?.IsWatching == true)
        {
            _folderWatcher.Stop();
            txtStatus.Text = "Đã tắt theo dõi thư mục.";
            return;
        }
        var folder = _workspace.CurrentFolder;
        if (string.IsNullOrEmpty(folder)) return;

        _folderWatcher ??= new ImageTool.Shared.FolderWatcher();
        _folderWatcher.ImageAdded -= OnWatchedImageAdded;
        _folderWatcher.ImageAdded += OnWatchedImageAdded;
        _folderWatcher.Start(folder);
        txtStatus.Text = $"Đang theo dõi: {System.IO.Path.GetFileName(folder)} (ảnh mới sẽ tự nạp).";
    }

    private void OnWatchedImageAdded(object? sender, string path)
    {
        // FolderWatcher chạy ở thread nền -> đưa về UI thread.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var folder = _folderWatcher?.Folder;
                if (folder != null && string.Equals(_workspace.CurrentFolder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    _workspace.OpenFolder(folder);          // refresh danh sách
                    _workspace.SetActiveImage(path);
                    _workspace.SetSelection(new[] { path });
                    txtStatus.Text = $"Ảnh mới: {System.IO.Path.GetFileName(path)}";
                }
            }
            catch (Exception ex) { AppLog.Warn("MainWindow.OnWatchedImageAdded", ex.Message); }
        });
    }

    private void BtnRecent_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // Recent Images section
        if (_settings.Current.RecentImages.Count > 0)
        {
            var header = new System.Windows.Controls.MenuItem { Header = "Recent Images", IsEnabled = false };
            menu.Items.Add(header);
            foreach (var path in _settings.Current.RecentImages.Take(10))
            {
                var mi = new System.Windows.Controls.MenuItem
                {
                    Header = System.IO.Path.GetFileName(path),
                    ToolTip = path
                };
                var captured = path;
                mi.Click += (s, ee) =>
                {
                    if (System.IO.File.Exists(captured))
                    {
                        var dir = System.IO.Path.GetDirectoryName(captured);
                        if (dir != null) _workspace.OpenFolder(dir);
                        Dispatcher.BeginInvoke(() => _workspace.SetActiveImage(captured),
                            System.Windows.Threading.DispatcherPriority.Background);
                    }
                };
                menu.Items.Add(mi);
            }
            menu.Items.Add(new System.Windows.Controls.Separator());
        }

        // Recent Folders section
        if (_settings.Current.RecentFolders.Count == 0)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "(empty)", IsEnabled = false });
        }
        else
        {
            var header = new System.Windows.Controls.MenuItem { Header = "Recent Folders", IsEnabled = false };
            menu.Items.Add(header);
            foreach (var path in _settings.Current.RecentFolders)
            {
                var mi = new System.Windows.Controls.MenuItem { Header = path };
                var captured = path;
                mi.Click += (s, ee) =>
                {
                    if (System.IO.Directory.Exists(captured))
                    {
                        _workspace.OpenFolder(captured);
                        _settings.AddRecentFolder(captured);
                    }
                };
                menu.Items.Add(mi);
            }
        }
        menu.PlacementTarget = btnRecent;
        menu.IsOpen = true;
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        var applied = ThemeManager.Toggle();
        _settings.Current.Theme = applied;
        _settings.Save();
        txtStatus.Text = $"Giao diện: {applied}";
    }

    // ===== Custom Window Chrome handlers =====
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BtnMaximize_Click(sender, e);
            return;
        }
        DragMove();
    }

    private void TitleBar_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // No-op needed; DragMove() handles it in MouseLeftButtonDown
    }

    // ===== Drag & Drop =====
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tiff", ".tif", ".bmp", ".gif", ".webp", ".ico",
        ".psd", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".rw2", ".orf", ".pef", ".srw"
    };

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            dropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        dropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        dropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        // Nếu thả 1 thư mục -> mở trong workspace
        if (files.Length == 1 && Directory.Exists(files[0]))
        {
            _workspace.OpenFolder(files[0]);
            return;
        }

        // Lọc file ảnh được hỗ trợ
        var imageFiles = files.Where(f =>
            File.Exists(f) && SupportedImageExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (imageFiles.Count == 0)
        {
            ShowToast("Không tìm thấy file ảnh được hỗ trợ.");
            return;
        }

        // Nếu chỉ có 1 ảnh -> mở thư mục chứa nó và set active
        if (imageFiles.Count == 1)
        {
            var dir = Path.GetDirectoryName(imageFiles[0]);
            if (dir != null)
            {
                _workspace.OpenFolder(dir);
                // Delay nhỏ để folder load xong rồi set active
                Dispatcher.BeginInvoke(() => _workspace.SetActiveImage(imageFiles[0]),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            return;
        }

        // Nhiều ảnh -> mở thư mục chứa ảnh đầu tiên
        var firstDir = Path.GetDirectoryName(imageFiles[0]);
        if (firstDir != null) _workspace.OpenFolder(firstDir);
        ShowToast($"Đã nhận {imageFiles.Count} ảnh.");
    }

    private void BtnPopOutTools_Click(object sender, RoutedEventArgs e)
    {        if (_toolsWindow != null)
        {
            _toolsWindow.Activate();
            return;
        }

        var parent = toolsHost.Parent as Grid;
        if (parent == null) return;

        _toolsHostOriginalParent = parent;
        _toolsHostOriginalColumn = Grid.GetColumn(toolsHost);
        parent.Children.Remove(toolsHost);

        _toolsWindow = new ToolsWindow { Owner = this };
        _toolsWindow.HostContent(toolsHost);

        PlaceOnSecondaryMonitor(_toolsWindow);

        _toolsWindow.Closed += (s, args) =>
        {
            _toolsWindow?.DetachContent();
            if (_toolsHostOriginalParent is Grid origParent && toolsHost.Parent == null)
            {
                Grid.SetColumn(toolsHost, _toolsHostOriginalColumn);
                origParent.Children.Add(toolsHost);
            }
            _toolsWindow = null;
            btnPopOutTools.IsEnabled = true;
        };

        _toolsWindow.Show();
        btnPopOutTools.IsEnabled = false;
        Closed += (s, args) => _toolsWindow?.Close();
    }

    private void ModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressModeSync) return;
        if (sender is RadioButton rb && rb.Tag is string tag &&
            Enum.TryParse<LighttableMode>(tag, out var mode))
        {
            _suppressModeSync = true;
            centerView.SwitchMode(mode);
            _suppressModeSync = false;
        }
    }

    private void CmbQuickRating_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace == null || cmbQuickRating == null) return;
        _workspace.Filter.MinRating = cmbQuickRating.SelectedIndex;
        _workspace.ApplyFilterAndSort();
    }

    private void QuickLabel_Click(object sender, RoutedEventArgs e)
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

    // ===== Nhớ bề rộng panel trái/phải (11.10) =====
    private void RestorePanelLayout()
    {
        var s = _settings.Current;
        if (s.LeftPanelWidth >= colLeft.MinWidth && s.LeftPanelWidth > 0)
            colLeft.Width = new GridLength(s.LeftPanelWidth);
        if (s.RightPanelWidth >= colRight.MinWidth && s.RightPanelWidth > 0)
            colRight.Width = new GridLength(s.RightPanelWidth);
    }

    private void SavePanelLayout()
    {
        try
        {
            double left = colLeft.ActualWidth, right = colRight.ActualWidth;
            if (left <= 0 || right <= 0) return;
            var s = _settings.Current;
            if (Math.Abs(s.LeftPanelWidth - left) > 0.5 || Math.Abs(s.RightPanelWidth - right) > 0.5)
            {
                s.LeftPanelWidth = left;
                s.RightPanelWidth = right;
                _settings.Save();
            }
        }
        catch { /* không chặn đóng app */ }
    }

    private void FilterPick_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null) return;
        // Toggle "chỉ hiện Pick". Bật cái này tự tắt Reject + HideRejected (loại trừ nhau).
        bool on = _workspace.Filter.RequiredPick != PickFlag.Pick;
        _workspace.Filter.RequiredPick = on ? PickFlag.Pick : (PickFlag?)null;
        if (on) _workspace.Filter.HideRejected = false;
        SyncPickFilterButtons();
        _workspace.ApplyFilterAndSort();
    }

    private void FilterReject_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null) return;
        bool on = _workspace.Filter.RequiredPick != PickFlag.Reject;
        _workspace.Filter.RequiredPick = on ? PickFlag.Reject : (PickFlag?)null;
        if (on) _workspace.Filter.HideRejected = false;
        SyncPickFilterButtons();
        _workspace.ApplyFilterAndSort();
    }

    private void HideReject_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null) return;
        bool on = !_workspace.Filter.HideRejected;
        _workspace.Filter.HideRejected = on;
        // Ẩn Reject mâu thuẫn với "chỉ hiện Reject" -> gỡ bộ lọc pick nếu nó đang là Reject.
        if (on && _workspace.Filter.RequiredPick == PickFlag.Reject)
            _workspace.Filter.RequiredPick = null;
        SyncPickFilterButtons();
        _workspace.ApplyFilterAndSort();
    }

    /// <summary>Cập nhật trạng thái "đang bật" (nền sáng) cho 3 nút lọc cờ.</summary>
    private void SyncPickFilterButtons()
    {
        if (_workspace == null) return;
        var on = ThemeManager.GetBrush("AccentBrush");
        var off = System.Windows.Media.Brushes.Transparent;
        if (btnFilterPick != null)
            btnFilterPick.Background = _workspace.Filter.RequiredPick == PickFlag.Pick ? on : off;
        if (btnFilterReject != null)
            btnFilterReject.Background = _workspace.Filter.RequiredPick == PickFlag.Reject ? on : off;
        if (btnHideReject != null)
            btnHideReject.Background = _workspace.Filter.HideRejected ? on : off;
    }

    private void PlaceOnSecondaryMonitor(Window w)
    {
        // Đặt cửa sổ Tools vào virtual screen bên phải MainWindow nếu có monitor khác.
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsWidth = SystemParameters.VirtualScreenWidth;
        double primaryWidth = SystemParameters.PrimaryScreenWidth;
        double mainRight = Left + ActualWidth;

        if (vsWidth > primaryWidth + 50 && mainRight + w.Width <= vsLeft + vsWidth)
        {
            w.Left = mainRight;
            w.Top = Top;
        }
        else
        {
            w.Left = Left + 80;
            w.Top = Top + 80;
        }
    }
}

public class PluginEntry
{
    public IImagePlugin Plugin { get; }
    public string Name => Plugin.Name;
    public string ShortName { get; }
    public string Glyph { get; }

    public PluginEntry(IImagePlugin plugin)
    {
        Plugin = plugin;
        var n = plugin.Name ?? "?";
        ShortName = n.Length > 10 ? n.Substring(0, 10) : n;
        Glyph = n.ToLowerInvariant() switch
        {
            var s when s.Contains("upscal") => "⬆",
            var s when s.Contains("face") => "☺",
            var s when s.Contains("color") => "◐",
            var s when s.Contains("meta") => "ℹ",
            var s when s.Contains("vision") || s.Contains("tag") => "✦",
            _ => "▣"
        };
    }
}
