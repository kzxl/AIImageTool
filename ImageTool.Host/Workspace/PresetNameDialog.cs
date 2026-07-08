using System.Windows;
using System.Windows.Controls;

namespace ImageTool.Host.Workspace;

/// <summary>Dialog nhỏ nhập tên preset Develop.</summary>
public class PresetNameDialog : Window
{
    private readonly TextBox _input;
    public string? PresetName { get; private set; }

    public PresetNameDialog()
    {
        Title = "Lưu Preset";
        Width = 320;
        Height = 140;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = ThemeManager.GetBrush("BgPanelBrush");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Tên preset:", Foreground = ThemeManager.GetBrush("TextPrimaryBrush"), Margin = new Thickness(0, 0, 0, 6) });
        _input = new TextBox { Height = 26, FontSize = 13 };
        root.Children.Add(_input);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Lưu", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Hủy", Width = 70, IsCancel = true };
        ok.Click += (_, _) => { PresetName = _input.Text; DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _input.Focus();
    }
}
