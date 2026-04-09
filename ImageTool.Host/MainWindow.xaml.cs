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
    private IEnumerable<IImagePlugin> _plugins;

    public MainWindow(PluginLoader pluginLoader, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _pluginLoader = pluginLoader;
        _serviceProvider = serviceProvider;
        
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