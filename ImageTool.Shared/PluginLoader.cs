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
