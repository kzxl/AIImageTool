using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ImageTool.Host;

/// <summary>
/// Converter Boolean -> Visibility ĐẢO: true => Collapsed, false => Visible.
/// Dùng cho empty-state hint (hiện chữ gợi ý khi danh sách RỖNG, tức HasItems == false).
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        return b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility vis && vis != Visibility.Visible;
}
