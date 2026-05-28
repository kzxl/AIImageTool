using System;
using ImageTool.Core;

namespace ImageTool.Plugins.FaceRestorer;

public class FaceRestorerPlugin : IImagePlugin
{
    private IServiceProvider _serviceProvider = null!;
    private readonly FaceRestorerControl _uiComponent;

    public FaceRestorerPlugin()
    {
        _uiComponent = new FaceRestorerControl();
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _uiComponent.AttachServices(serviceProvider);
    }

    public string Name => "AI Face Restorer";
    public string Description => "Phục hồi nét chân dung (GPEN-BFR-512 ONNX)";
    public string Version => "1.0.0";

    public object GetUIComponent() => _uiComponent;
}
