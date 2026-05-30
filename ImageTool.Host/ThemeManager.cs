using System;
using System.Windows;

namespace ImageTool.Host;

/// <summary>
/// Quản lý đổi theme runtime (#8). Swap ResourceDictionary trong Application.Resources giữa
/// Dark/Light. Vì các style + brush trong theme đều dùng StaticResource nội bộ dictionary, đổi cả
/// dictionary sẽ áp lại toàn bộ control dùng implicit style + DynamicResource.
///
/// GIỚI HẠN: các control đặt màu hex hardcode trực tiếp trong XAML (≈113 chỗ) vẫn giữ màu tối —
/// Light theme là "experimental", cần migrate dần sang DynamicResource để hoàn chỉnh.
/// </summary>
public static class ThemeManager
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    private static string _current = Dark;
    public static string Current => _current;

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
