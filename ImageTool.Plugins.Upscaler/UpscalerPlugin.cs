using System;
using ImageTool.Core;

namespace ImageTool.Plugins.Upscaler;

public class UpscalerPlugin : IImagePlugin
{
    public string Name => "AI Upscaler";
    public string Version => "1.0.0";
    public string Description => "Upscale images using ONNX Real-ESRGAN or similar model.";

    private IServiceProvider _serviceProvider;
    private UpscalerControl _uiComponent;

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _uiComponent = new UpscalerControl();
        // Cấu hình Model tại đây, có thể inject từ serviceProvider
    }

    public object GetUIComponent()
    {
        return _uiComponent;
    }
}
