using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImageTool.Core;
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
    private List<PluginEntry> _pluginEntries = new();
    private ToolsWindow? _toolsWindow;
    private UIElement? _toolsHostOriginalParent;
    private int _toolsHostOriginalRow;

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
        IStyleService styles)
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

        // Load saved batch parallel
        _batch.MaxParallel = Math.Max(1, _settings.Current.BatchParallel);

        browser.Bind(_workspace, _thumbs, _meta);
        centerView.Bind(_workspace, _thumbs, _meta);
        filmstrip.Bind(_workspace, _thumbs, _meta);
        infoPanel.Bind(_workspace);
        historyPanel.Bind(_workspace, _history);
        batchPanel.Bind(_batch);
        exportPanel.Bind(_workspace, _batch);
        stylePanel.Bind(_styles, _workspace, _batch);

        _aiManager.StartWorker();
        Closed += (s, e) =>
        {
            _aiManager.Dispose();
            (_thumbs as IDisposable)?.Dispose();
        };

        _workspace.FolderOpened += (s, e) =>
            Dispatcher.BeginInvoke(() => txtFolder.Text = e.FolderPath);

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        LoadPlugins();
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_workspace.ActiveImage == null) return;
        var path = _workspace.ActiveImage;
        bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;

        if (ctrl)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Z: _history.Undo(path); e.Handled = true; return;
                case System.Windows.Input.Key.Y: _history.Redo(path); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            case System.Windows.Input.Key.D0: _meta.SetRating(path, 0); e.Handled = true; break;
            case System.Windows.Input.Key.D1: _meta.SetRating(path, 1); e.Handled = true; break;
            case System.Windows.Input.Key.D2: _meta.SetRating(path, 2); e.Handled = true; break;
            case System.Windows.Input.Key.D3: _meta.SetRating(path, 3); e.Handled = true; break;
            case System.Windows.Input.Key.D4: _meta.SetRating(path, 4); e.Handled = true; break;
            case System.Windows.Input.Key.D5: _meta.SetRating(path, 5); e.Handled = true; break;
            case System.Windows.Input.Key.P: _meta.SetPick(path, PickFlag.Pick); e.Handled = true; break;
            case System.Windows.Input.Key.X: _meta.SetPick(path, PickFlag.Reject); e.Handled = true; break;
            case System.Windows.Input.Key.U: _meta.SetPick(path, PickFlag.None); e.Handled = true; break;
            case System.Windows.Input.Key.D6: _meta.SetLabel(path, ColorLabel.Red); e.Handled = true; break;
            case System.Windows.Input.Key.D7: _meta.SetLabel(path, ColorLabel.Yellow); e.Handled = true; break;
            case System.Windows.Input.Key.D8: _meta.SetLabel(path, ColorLabel.Green); e.Handled = true; break;
            case System.Windows.Input.Key.D9: _meta.SetLabel(path, ColorLabel.Blue); e.Handled = true; break;
        }
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

    private void BtnPopOutTools_Click(object sender, RoutedEventArgs e)
    {
        if (_toolsWindow != null)
        {
            _toolsWindow.Activate();
            return;
        }

        var parent = toolsHost.Parent as Grid;
        if (parent == null) return;

        _toolsHostOriginalParent = parent;
        _toolsHostOriginalRow = Grid.GetRow(toolsHost);
        parent.Children.Remove(toolsHost);

        _toolsWindow = new ToolsWindow { Owner = this };
        _toolsWindow.HostContent(toolsHost);

        PlaceOnSecondaryMonitor(_toolsWindow);

        _toolsWindow.Closed += (s, args) =>
        {
            _toolsWindow?.DetachContent();
            if (_toolsHostOriginalParent is Grid origParent && toolsHost.Parent == null)
            {
                Grid.SetRow(toolsHost, _toolsHostOriginalRow);
                origParent.Children.Add(toolsHost);
            }
            _toolsWindow = null;
        };

        _toolsWindow.Show();
        btnPopOutTools.IsEnabled = false;
        Closed += (s, args) => _toolsWindow?.Close();
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
