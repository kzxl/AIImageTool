using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

public class TestLoader : AssemblyLoadContext
{
    private AssemblyDependencyResolver _resolver;
    private string _pluginDir;
    public TestLoader(string path) {
        _resolver = new AssemblyDependencyResolver(path);
        _pluginDir = Path.GetDirectoryName(path);
    }
    protected override Assembly Load(AssemblyName n)
    {
        if (n.Name == "ImageTool.Core" || n.Name == "ImageTool.Shared") return AssemblyLoadContext.Default.LoadFromAssemblyName(n);
        var path = _resolver.ResolveAssemblyToPath(n);
        Console.WriteLine("RESOLVER Path for "+n.Name+": "+path);
        if (path != null) return LoadFromAssemblyPath(path);
        var fallback = Path.Combine(_pluginDir, n.Name + ".dll");
        Console.WriteLine("FALLBACK Path for "+n.Name+": "+fallback);
        if (File.Exists(fallback)) return LoadFromAssemblyPath(fallback);
        return null;
    }
}

public class Program
{
    public static void Main()
    {
        var dll = @"e:\15. Other\ImageTool\ImageTool.Host\bin\Debug\net8.0-windows\Plugins\ImageTool.Plugins.Upscaler.dll";
        var ctx = new TestLoader(dll);
        var asm = ctx.LoadFromAssemblyName(new AssemblyName("ImageTool.Plugins.Upscaler"));
        Console.WriteLine("Loaded plugin: " + asm.FullName);
        var type = asm.GetType("ImageTool.Plugins.Upscaler.UpscalerControl");
        Console.WriteLine("Loaded type: " + (type != null));
        
        try {
            var mth = ctx.LoadFromAssemblyName(new AssemblyName("SixLabors.ImageSharp"));
            Console.WriteLine("Loaded ImageSharp: " + (mth != null));
        } catch(Exception e) { Console.WriteLine(e); }
    }
}
