using System;
using ImageTool.Core;

namespace ImageTool.Plugins.ColorLab;

public class ColorLabPlugin : IImagePlugin
{
    public string Name => "Color Lab & Analyzer";
    public string Version => "1.0.0";
    public string Description => "Extract dominant color palettes and perform selective color grading (HSL Shift).";

    private IServiceProvider _serviceProvider = null!;
    private ColorLabControl _uiComponent = null!;

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _uiComponent = new ColorLabControl();
        _uiComponent.AttachServices(serviceProvider);
    }

    public object GetUIComponent()
    {
        return _uiComponent;
    }
}
