using System;
using ImageTool.Core;

namespace ImageTool.Plugins.FaceRestorer;

public class FaceRestorerPlugin : IImagePlugin
{
    private IServiceProvider _serviceProvider = null!;
    private readonly FaceRestorerControl _uiComponent;

    public FaceRestorerPlugin()
    {
        // Khởi tạo thành phần UI
        _uiComponent = new FaceRestorerControl();
    }

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string Name => "AI Face Restorer";
    public string Description => "Phục hồi nét siêu căng cho khuôn mặt (GFPGAN)";
    public string Version => "1.0.0";
    
    public object GetUIComponent() => _uiComponent;
}
