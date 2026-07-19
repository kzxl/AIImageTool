using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NLua;

namespace ImageTool.Imaging;

/// <summary>
/// Edit Operation cho phép chạy script Lua tùy biến để xử lý LinearImage.
/// Hỗ trợ cả chỉnh sửa pixel tại chỗ (in-place) và các thao tác thay đổi kích thước (resizing).
/// Hỗ trợ nạp script động từ các file .lua độc lập trong thư mục Scripts hoặc truyền inline.
/// </summary>
public sealed class LuaScriptOp : IResizingOp
{
    public const string Type = "LuaScript";
    public string OpType => Type;

    private readonly string _script;
    private readonly string _scriptName;
    private readonly Dictionary<string, string> _params;

    public LuaScriptOp(string script, Dictionary<string, string> @params, string scriptName = "")
    {
        _script = script;
        _params = @params;
        _scriptName = scriptName;
    }

    public void Apply(LinearImage image, float scale)
    {
        ExecuteScript(image, scale);
    }

    public LinearImage ApplyResize(LinearImage image, float scale)
    {
        return ExecuteScript(image, scale);
    }

    private LinearImage ExecuteScript(LinearImage image, float scale)
    {
        try
        {
            string scriptContent = GetScriptContent();
            if (string.IsNullOrWhiteSpace(scriptContent)) return image;

            using var lua = new Lua();
            lua.State.Encoding = System.Text.Encoding.UTF8;

            // 1. Truyền các thông tin của LinearImage vào môi trường Lua
            lua["pixels"] = image.Pixels;
            lua["width"] = image.Width;
            lua["height"] = image.Height;
            lua["scale"] = scale;

            // 2. Đăng ký các helper C# tối ưu hóa
            lua.RegisterFunction("AdjustBrightnessContrast", null, typeof(LuaImageHelper).GetMethod(nameof(LuaImageHelper.AdjustBrightnessContrast)));
            lua.RegisterFunction("FlipHorizontal", null, typeof(LuaImageHelper).GetMethod(nameof(LuaImageHelper.FlipHorizontal)));
            lua.RegisterFunction("Rotate90CW", null, typeof(LuaImageHelper).GetMethod(nameof(LuaImageHelper.Rotate90CW)));

            // 3. Truyền các tham số động từ Params
            foreach (var kvp in _params)
            {
                if (kvp.Key is "pixels" or "width" or "height" or "scale" or "script" or "script_name")
                    continue;

                if (double.TryParse(kvp.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    lua[kvp.Key] = d;
                }
                else if (bool.TryParse(kvp.Value, out var b))
                {
                    lua[kvp.Key] = b;
                }
                else
                {
                    lua[kvp.Key] = kvp.Value;
                }
            }

            // 4. Thực thi script
            lua.DoString(scriptContent);

            // 5. Đọc lại các biến toàn cục sau khi chạy để cập nhật LinearImage (nếu đổi kích thước hoặc đổi mảng)
            var luaPixels = lua["pixels"] as float[];
            var luaWidth = Convert.ToInt32(lua["width"]);
            var luaHeight = Convert.ToInt32(lua["height"]);

            if (luaPixels == null)
            {
                throw new InvalidOperationException("Script Lua không được gán pixels thành null.");
            }

            // Nếu kích thước hoặc mảng thay đổi, trả về LinearImage mới
            if (luaPixels != image.Pixels || luaWidth != image.Width || luaHeight != image.Height)
            {
                return new LinearImage(luaWidth, luaHeight, luaPixels);
            }
        }
        catch (Exception ex)
        {
            LogError($"Lỗi thực thi script Lua '{_scriptName}'", ex);
        }

        return image;
    }

    private string GetScriptContent()
    {
        if (!string.IsNullOrWhiteSpace(_scriptName))
        {
            string name = _scriptName;
            if (!name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            {
                name += ".lua";
            }

            // Thử tìm file script ở một số thư mục thông dụng
            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", name),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name),
                Path.Combine(Environment.CurrentDirectory, "Scripts", name),
                Path.Combine(Environment.CurrentDirectory, name)
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }

            // Nếu không tìm thấy file, fallback về script inline nếu có
            if (!string.IsNullOrWhiteSpace(_script))
            {
                return _script;
            }

            throw new FileNotFoundException($"Không tìm thấy file Lua script '{_scriptName}' ở bất kỳ thư mục tìm kiếm nào.");
        }

        return _script;
    }

    private static void LogError(string message, Exception ex)
    {
        try
        {
            // Sử dụng Reflection để gọi AppLog.Error động, tránh circular dependency
            var appLogType = System.Type.GetType("ImageTool.Shared.AppLog, ImageTool.Shared");
            if (appLogType != null)
            {
                var errorMethod = appLogType.GetMethod("Error", new[] { typeof(string), typeof(string), typeof(Exception) });
                errorMethod?.Invoke(null, new object[] { "LuaScriptOp", message, ex });
            }
            else
            {
                Console.WriteLine($"[LuaScriptOp ERROR] {message}: {ex.Message}");
            }
        }
        catch
        {
            Console.WriteLine($"[LuaScriptOp ERROR] {message}: {ex.Message}");
        }
    }

    public Dictionary<string, string> ToParams()
    {
        var dict = new Dictionary<string, string>(_params);
        if (!string.IsNullOrEmpty(_script))
        {
            dict["script"] = _script;
        }
        if (!string.IsNullOrEmpty(_scriptName))
        {
            dict["script_name"] = _scriptName;
        }
        return dict;
    }

    public static LuaScriptOp FromParams(IReadOnlyDictionary<string, string> p)
    {
        string script = EditOpRegistry.S(p, "script", "");
        string scriptName = EditOpRegistry.S(p, "script_name", "");
        var paramsCopy = new Dictionary<string, string>();
        foreach (var kvp in p)
        {
            if (kvp.Key != "script" && kvp.Key != "script_name")
            {
                paramsCopy[kvp.Key] = kvp.Value;
            }
        }
        return new LuaScriptOp(script, paramsCopy, scriptName);
    }

    public static void Register(EditOpRegistry reg)
    {
        reg.Register(Type, FromParams);
    }
}
