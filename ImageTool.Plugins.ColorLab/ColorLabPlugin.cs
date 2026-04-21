using System;
using ImageTool.Core;

namespace ImageTool.Plugins.ColorLab;

public class ColorLabPlugin : IImagePlugin
{
    public string Name => "Color Lab & Analyzer";
    public string Version => "1.0.0";
    public string Description => "Extract dominant color palettes and perform selective color grading (HSL Shift).";

    private IServiceProvider _serviceProvider;
    private ColorLabControl _uiComponent;

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _uiComponent = new ColorLabControl();
    }

    public object GetUIComponent()
    {
        return _uiComponent;
    }
}
