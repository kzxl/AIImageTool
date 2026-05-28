using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ImageTool.Core;
using ImageTool.Shared;
namespace ImageTool.Host;

public partial class App : Application
{
    private IServiceProvider _serviceProvider;
    private static readonly string CrashLogPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "crash.log");

    public App()
    {
        this.DispatcherUnhandledException += (s, e) =>
        {
            LogException("UI Crash (Dispatcher)", e.Exception);
            MessageBox.Show(
                $"Lỗi giao diện nghiêm trọng:\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\nLog: {CrashLogPath}",
                "Crash Insulator", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogException("AppDomain Crash", e.ExceptionObject as Exception, $"IsTerminating={e.IsTerminating}");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogException("Unobserved Task Exception", e.Exception);
            e.SetObserved();
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void LogException(string source, Exception? ex, string? extra = null)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source} ===");
            if (extra != null) sb.AppendLine(extra);
            DumpException(sb, ex, depth: 0);
            sb.AppendLine();
            System.IO.File.AppendAllText(CrashLogPath, sb.ToString());
        }
        catch { /* logging itself must never throw */ }
    }

    private static void DumpException(System.Text.StringBuilder sb, Exception? ex, int depth)
    {
        if (ex == null) { sb.AppendLine("(null exception)"); return; }
        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}[{ex.GetType().FullName}] {ex.Message}");
        if (ex.TargetSite != null)
            sb.AppendLine($"{indent}  at: {ex.TargetSite.DeclaringType?.FullName}.{ex.TargetSite.Name}");
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            foreach (var line in ex.StackTrace.Split('\n'))
                sb.AppendLine($"{indent}  {line.TrimEnd()}");
        }
        if (ex is AggregateException agg)
        {
            int i = 0;
            foreach (var inner in agg.InnerExceptions)
            {
                sb.AppendLine($"{indent}-- Aggregate inner #{i++} --");
                DumpException(sb, inner, depth + 1);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine($"{indent}-- Inner --");
            DumpException(sb, ex.InnerException, depth + 1);
        }
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
        try
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
        catch (Exception ex)
        {
            LogException("Startup Failed", ex);
            MessageBox.Show(
                $"Không khởi động được app:\n{ex.GetType().Name}: {ex.Message}\n\nLog: {CrashLogPath}",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
