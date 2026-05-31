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
        developPanel.Bind(_workspace, _history, centerView.Renderer, _developClipboard, _styles,
            serviceProvider.GetRequiredService<LensfunService>());
        centerView.BindCropPanel(developPanel);
        centerView.BindBrushPanel(developPanel);
        centerView.BindWhiteBalancePick(developPanel);
        centerView.BindHealingPanel(developPanel);
        centerView.BindLiquifyPanel(developPanel);

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
        _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); toastBorder.Visibility = Visibility.Collapsed; };
        // Báo khi job batch xong (export/style/upscale...).
        _batch.JobUpdated += (s, job) =>
        {
            if (job.Status == BatchJobStatus.Completed)
                Dispatcher.BeginInvoke(() => ShowToast($"Xong: {job.DisplayName}"));
            else if (job.Status == BatchJobStatus.Failed)
                Dispatcher.BeginInvoke(() => ShowToast($"Lỗi: {job.DisplayName}"));
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
            toastBorder.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        });
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

        foreach (var plugin in plugins)
        {
            try { plugin.Initialize(_serviceProvider); } catch { }
        }

        _pluginEntries = plugins
            .Select(p => new PluginEntry(p))
            .ToList();

        lstPlugins.ItemsSource = _pluginEntries;
        if (_pluginEntries.Count > 0) lstPlugins.SelectedIndex = 0;
    }

    private void LstPlugins_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstPlugins.SelectedItem is PluginEntry entry)
        {
            try { contentPresenter.Content = entry.Plugin.GetUIComponent(); }
            catch { contentPresenter.Content = null; }
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

        menu.Items.Add(new System.Windows.Controls.Separator());

        var miRename = new System.Windows.Controls.MenuItem { Header = "Batch Rename...", IsEnabled = targets.Count >= 1 };
        miRename.Click += (_, _) => ImageContextMenu.RunBatchRename(targets);
        menu.Items.Add(miRename);

        if (targets.Count == 0)
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "(chọn ảnh trước)", IsEnabled = false });

        menu.PlacementTarget = btnMore;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BtnRecent_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        if (_settings.Current.RecentFolders.Count == 0)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "(empty)", IsEnabled = false });
        }
        else
        {
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
        var on = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x7E, 0xFF));
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
