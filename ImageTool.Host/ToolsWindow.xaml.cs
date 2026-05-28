using System.Windows;
using System.Windows.Controls;

namespace ImageTool.Host;

public partial class ToolsWindow : Window
{
    public ToolsWindow()
    {
        InitializeComponent();
    }

    /// <summary>Đặt UI tool plugin từ MainWindow vào cửa sổ này (host).</summary>
    public void HostContent(UIElement content)
    {
        rootHost.Children.Clear();
        rootHost.Children.Add(content);
    }

    public void DetachContent()
    {
        rootHost.Children.Clear();
    }
}
