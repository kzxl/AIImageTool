using System;
using ImageTool.Core;
using Microsoft.Extensions.DependencyInjection;

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

        // Đăng ký batch capability vào BatchService nếu có
        var batch = serviceProvider.GetService<IBatchService>();
        batch?.RegisterCapability(new UpscalerBatchAdapter());

        // Pass services xuống UI control để gọi IBatchService + IWorkspaceService
        _uiComponent.AttachServices(serviceProvider);
    }

    public object GetUIComponent()
    {
        return _uiComponent;
    }
}
