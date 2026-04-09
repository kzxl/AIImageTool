using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ImageTool.Core;
using ImageTool.Shared;

namespace ImageTool.Host;

public partial class App : Application
{
    private IServiceProvider _serviceProvider;

    public App()
    {
        this.DispatcherUnhandledException += (s, e) => {
            System.IO.File.AppendAllText("crash.log", $"[UI Crash] {e.Exception}\n");
            MessageBox.Show($"Lỗi giao diện nghiêm trọng:\n{e.Exception.Message}", "Crash Insulator", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            System.IO.File.AppendAllText("crash.log", $"[AppDomain Crash] {e.ExceptionObject}\n");
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // View
        services.AddSingleton<MainWindow>();

        // Core/Shared Services
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<PluginLoader>();
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
