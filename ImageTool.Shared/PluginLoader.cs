using System.Reflection;
using System.Runtime.Loader;
using ImageTool.Core;

namespace ImageTool.Shared;

public class PluginAssemblyLoadContext : AssemblyLoadContext
{
    public string PluginPath { get; }
    private readonly AssemblyDependencyResolver _resolver;

    public PluginAssemblyLoadContext(string pluginPath) 
        : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: true)
    {
        PluginPath = pluginPath;
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Tránh tải lại các assembly cốt lõi để tránh conflict và lỗi ép kiểu
        if (assemblyName.Name == null ||
            assemblyName.Name == "ImageTool.Core" || 
            assemblyName.Name == "ImageTool.Shared" ||
            assemblyName.Name == "ImageTool.Imaging" ||
            assemblyName.Name.StartsWith("System.") ||
            assemblyName.Name.StartsWith("Microsoft."))
        {
            return null; // Delegate về Default Context
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}

public class PluginLoader
{
    /// <summary>
    /// Lỗi gặp khi nạp plugin ở lần <see cref="LoadPlugins"/> gần nhất (file DLL -> thông điệp lỗi).
    /// Host đọc danh sách này để báo cho user thay vì nuốt im lặng.
    /// </summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;
    private readonly List<string> _loadErrors = new();

    private static readonly List<PluginAssemblyLoadContext> _activeContexts = new();
    private static bool _hooksRegistered = false;

    private static void RegisterResolvingHooks(string pluginsPath)
    {
        if (_hooksRegistered) return;
        _hooksRegistered = true;

        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            if (assemblyName.Name == null) return null;
            var files = Directory.GetFiles(pluginsPath, $"{assemblyName.Name}.dll", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                string targetPath = files[0];
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir == null) return null;

                var alc = _activeContexts.FirstOrDefault(c => 
                {
                    string? dir = Path.GetDirectoryName(c.PluginPath);
                    return dir != null && string.Equals(dir, targetDir, StringComparison.OrdinalIgnoreCase);
                });

                if (alc != null)
                {
                    try
                    {
                        return alc.LoadFromAssemblyPath(targetPath);
                    }
                    catch { }
                }
            }
            return null;
        };

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            if (assemblyName.Name == null) return null;
            var files = Directory.GetFiles(pluginsPath, $"{assemblyName.Name}.dll", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                string targetPath = files[0];
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir == null) return null;

                var alc = _activeContexts.FirstOrDefault(c => 
                {
                    string? dir = Path.GetDirectoryName(c.PluginPath);
                    return dir != null && string.Equals(dir, targetDir, StringComparison.OrdinalIgnoreCase);
                });

                if (alc != null)
                {
                    try
                    {
                        return alc.LoadFromAssemblyPath(targetPath);
                    }
                    catch { }
                }
            }
            return null;
        };
    }

    public IEnumerable<IImagePlugin> LoadPlugins(string pluginsPath)
    {
        var plugins = new List<IImagePlugin>();
        _loadErrors.Clear();

        if (!Directory.Exists(pluginsPath))
        {
            Directory.CreateDirectory(pluginsPath);
            return plugins;
        }

        // Dọn dẹp các context cũ trước khi load mới (nếu có)
        foreach (var oldAlc in _activeContexts)
        {
            try
            {
                oldAlc.Unload();
            }
            catch { }
        }
        _activeContexts.Clear();

        RegisterResolvingHooks(pluginsPath);

        // Lọc chính xác các DLL là Plugin, không nạp nhầm các file thư viện rác (như SixLabors.ImageSharp.dll)
        var dllFiles = Directory.GetFiles(pluginsPath, "ImageTool.Plugins.*.dll", SearchOption.AllDirectories);
        foreach (var dllFile in dllFiles)
        {
            try
            {
                var alc = new PluginAssemblyLoadContext(dllFile);
                _activeContexts.Add(alc);

                var assembly = alc.LoadFromAssemblyPath(dllFile);
                
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
                var name = Path.GetFileNameWithoutExtension(dllFile);
                var msg = $"{name}: {ex.GetType().Name} - {ex.Message}";
                _loadErrors.Add(msg);
                AppLog.Error("PluginLoader", $"Nạp plugin thất bại: {dllFile}", ex);
            }
        }

        return plugins;
    }
}
