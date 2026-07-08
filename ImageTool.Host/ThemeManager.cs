using System;
using System.Windows;
using System.Windows.Media;

namespace ImageTool.Host;

/// <summary>
/// Quản lý đổi theme runtime. Swap ResourceDictionary trong Application.Resources giữa
/// Dark/Light. Các style + brush trong theme đều dùng DynamicResource, đổi dictionary sẽ
/// áp lại toàn bộ control dùng implicit style.
/// </summary>
public static class ThemeManager
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    private static string _current = Dark;
    public static string Current => _current;

    /// <summary>Lấy brush từ theme resource. Fallback về SolidColorBrush nếu không tìm thấy.</summary>
    public static Brush GetBrush(string key, Brush? fallback = null)
    {
        var app = Application.Current;
        if (app != null)
        {
            var resource = app.TryFindResource(key);
            if (resource is Brush brush) return brush;
        }
        return fallback ?? Brushes.Gray;
    }

    /// <summary>Lấy Color từ theme resource.</summary>
    public static Color GetColor(string key, Color fallback = default)
    {
        var app = Application.Current;
        if (app != null)
        {
            var resource = app.TryFindResource(key);
            if (resource is Color color) return color;
            if (resource is SolidColorBrush scb) return scb.Color;
        }
        return fallback;
    }

    /// <summary>Áp theme theo tên ("Dark"/"Light"). Bỏ qua nếu không đổi.</summary>
    public static void Apply(string theme)
    {
        theme = string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;
        var app = Application.Current;
        if (app == null) return;

        var uri = new Uri($"Themes/{theme}Theme.xaml", UriKind.Relative);
        ResourceDictionary dict;
        try { dict = new ResourceDictionary { Source = uri }; }
        catch { return; }

        // Xoá theme cũ (file nào trong Themes/) rồi chèn theme mới ở đầu để style khác override được.
        var merged = app.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.OriginalString ?? "";
            if (src.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(i);
        }
        merged.Insert(0, dict);
        _current = theme;
    }

    /// <summary>Đảo Dark &lt;-&gt; Light, trả tên mới.</summary>
    public static string Toggle()
    {
        var next = _current == Dark ? Light : Dark;
        Apply(next);
        return next;
    }
}
