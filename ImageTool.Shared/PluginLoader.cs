using System.Reflection;
using System.Runtime.Loader;
using ImageTool.Core;

namespace ImageTool.Shared;

public class PluginLoader
{
    public IEnumerable<IImagePlugin> LoadPlugins(string pluginsPath)
    {
        var plugins = new List<IImagePlugin>();

        if (!Directory.Exists(pluginsPath))
        {
            Directory.CreateDirectory(pluginsPath);
            return plugins;
        }

        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            var files = Directory.GetFiles(pluginsPath, $"{assemblyName.Name}.dll", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return context.LoadFromAssemblyPath(files[0]);
            }
            return null;
        };

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            var files = Directory.GetFiles(pluginsPath, $"{assemblyName.Name}.dll", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return Assembly.LoadFrom(files[0]);
            }
            return null;
        };

        // PRELOAD VÀO BỘ NHỚ LÕI: WPF BAML Loader rất ngu ngốc trong việc tự Resolve qua hook, nên ta bắt buộc phải nạp tất cả Dependencies vào Default Context trước!
        var allDlls = Directory.GetFiles(pluginsPath, "*.dll", SearchOption.AllDirectories);
        foreach (var dll in allDlls)
        {
            if (!Path.GetFileName(dll).StartsWith("ImageTool.Plugins."))
            {
                try
                {
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                }
                catch (Exception) { /* Bỏ qua nếu lỗi như thư viện native C++ */ }
            }
        }

        // Lọc chính xác các DLL là Plugin, không nạp nhầm các file thư viện rác (như SixLabors.ImageSharp.dll)
        var dllFiles = Directory.GetFiles(pluginsPath, "ImageTool.Plugins.*.dll", SearchOption.AllDirectories);
        foreach (var dllFile in dllFiles)
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllFile);
                
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IImagePlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var pluginType in pluginTypes)
                {
                    if (Activator.CreateInstance(pluginType) is IImagePlugin plugin)
                    {
                        plugins.Add(plugin);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load plugin from {dllFile}: {ex.Message}");
            }
        }

        return plugins;
    }
}
