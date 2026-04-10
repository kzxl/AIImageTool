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
    private IEnumerable<IImagePlugin> _plugins;

    public MainWindow(PluginLoader pluginLoader, IServiceProvider serviceProvider, AiWorkerManager aiManager)
    {
        InitializeComponent();
        _pluginLoader = pluginLoader;
        _serviceProvider = serviceProvider;
        _aiManager = aiManager;
        
        // Khởi động API Server ngầm không màn hình
        _aiManager.StartWorker();
        
        // Giết Python khi C# bị tắt
        this.Closed += (s, e) => _aiManager.Dispose();

        LoadPlugins();
    }

    private void LoadPlugins()
    {
        string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        _plugins = _pluginLoader.LoadPlugins(pluginsPath);

        foreach (var plugin in _plugins)
        {
            plugin.Initialize(_serviceProvider);
        }

        lstPlugins.ItemsSource = _plugins;
    }

    private void LstPlugins_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstPlugins.SelectedItem is IImagePlugin selectedPlugin)
        {
            contentPresenter.Content = selectedPlugin.GetUIComponent();
        }
    }
}