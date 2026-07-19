using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ImageTool.Host.Workspace;

/// <summary>
/// Hộp thoại cho phép người dùng chọn lựa các phần thông số muốn đồng bộ (Sync) giữa các ảnh.
/// Dựng giao diện động bằng code-behind để đồng bộ giao diện Dark Theme của app.
/// </summary>
public sealed class SyncSettingsDialog : Window
{
    private readonly Dictionary<string, CheckBox> _checkBoxes = new();

    public HashSet<string> SelectedCategories { get; } = new HashSet<string>();

    public SyncSettingsDialog()
    {
        Title = "Đồng bộ chỉnh ảnh (Sync Settings)";
        Width = 380;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.GetBrush("BgPanelBrush");
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16) };

        // Tiêu đề phía trên
        var title = new TextBlock
        {
            Text = "Chọn các thông số muốn đồng bộ:",
            Foreground = ThemeManager.GetBrush("TextPrimaryBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        // Hàng nút chọn nhanh (chọn tất cả / bỏ chọn)
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var btnAll = new Button { Content = "Chọn tất cả", Width = 90, Height = 22, Margin = new Thickness(0, 0, 8, 0), FontSize = 11 };
        var btnNone = new Button { Content = "Bỏ chọn tất cả", Width = 90, Height = 22, FontSize = 11 };
        btnAll.Click += (s, e) => ToggleAll(true);
        btnNone.Click += (s, e) => ToggleAll(false);
        actionRow.Children.Add(btnAll);
        actionRow.Children.Add(btnNone);
        
        // Hàng nút xác nhận ở dưới đáy
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Đồng bộ (Sync)", Width = 100, Height = 28, IsDefault = true, FontSize = 11 };
        var cancel = new Button { Content = "Hủy", Width = 80, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true, FontSize = 11 };
        ok.Click += OnSync;
        cancel.Click += (_, _) => Close();
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);

        var bottomArea = new StackPanel();
        bottomArea.Children.Add(actionRow);
        bottomArea.Children.Add(btnRow);
        DockPanel.SetDock(bottomArea, Dock.Bottom);
        root.Children.Add(bottomArea);

        // Phần danh sách CheckBox ở giữa
        var listPanel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        
        AddCheck(listPanel, "Basic", "Basic Adjustments (Exposure, Contrast, Saturation, WB...)");
        AddCheck(listPanel, "HSL", "HSL / Color Mixer");
        AddCheck(listPanel, "Detail", "Detail & Effects (Sharpen, Denoise, Glow, Vignette)");
        AddCheck(listPanel, "LUT", "3D LUT (Lookup Table)");
        AddCheck(listPanel, "Lua", "Lua Scripting (Các slider script động)");

        root.Children.Add(listPanel);
        Content = root;
    }

    private void AddCheck(Panel host, string key, string label)
    {
        var chk = new CheckBox
        {
            Content = label,
            IsChecked = true,
            Foreground = ThemeManager.GetBrush("TextSecondaryBrush"),
            Margin = new Thickness(0, 4, 0, 4),
            FontSize = 11
        };
        host.Children.Add(chk);
        _checkBoxes[key] = chk;
    }

    private void ToggleAll(bool check)
    {
        foreach (var chk in _checkBoxes.Values)
        {
            chk.IsChecked = check;
        }
    }

    private void OnSync(object sender, RoutedEventArgs e)
    {
        foreach (var kvp in _checkBoxes)
        {
            if (kvp.Value.IsChecked == true)
            {
                SelectedCategories.Add(kvp.Key);
            }
        }
        DialogResult = true;
        Close();
    }
}
