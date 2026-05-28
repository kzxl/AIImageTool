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
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IImageMetaService, ImageMetaService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<IBatchService, BatchService>();
        services.AddSingleton<IModelDownloader, ModelDownloader>();
        services.AddSingleton<IStyleService, StyleService>();
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<AiWorkerManager>();
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Register built-in capabilities
        var batch = _serviceProvider.GetService(typeof(IEventBus)) as IEventBus; // ensure DI built
        var batchSvc = (IBatchService)_serviceProvider.GetService(typeof(IBatchService))!;
        batchSvc?.RegisterCapability(new ImageTool.Shared.ExportBatchAdapter());

        var styleSvc = (IStyleService)_serviceProvider.GetService(typeof(IStyleService))!;
        if (styleSvc != null)
            batchSvc?.RegisterCapability(new ImageTool.Shared.StyleBatchAdapter(styleSvc));

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
