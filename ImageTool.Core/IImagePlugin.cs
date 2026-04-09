namespace ImageTool.Core;

public interface IImagePlugin
{
    string Name { get; }
    string Version { get; }
    string Description { get; }

    /// <summary>
    /// Lifecycle: Initialize plugin resources
    /// </summary>
    void Initialize(IServiceProvider serviceProvider);

    /// <summary>
    /// Get the main UI component (usually a WPF UserControl)
    /// </summary>
    object GetUIComponent();
}
