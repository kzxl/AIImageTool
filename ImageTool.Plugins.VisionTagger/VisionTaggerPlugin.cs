using System;
using System.Windows.Controls;
using ImageTool.Core;

namespace ImageTool.Plugins.VisionTagger;

public class VisionTaggerPlugin : IImagePlugin
{
    private VisionTaggerControl? _uiComponent;

    public string Name => "Auto Tagger";
    public string Version => "1.0.0";
    public string Description => "Phân tích nội dung và sinh tag bằng WD ViT v3 (ONNX local).";

    public void Initialize(IServiceProvider serviceProvider)
    {
        _uiComponent = new VisionTaggerControl();
        _uiComponent.AttachServices(serviceProvider);
    }

    public object GetUIComponent()
    {
        return _uiComponent ??= new VisionTaggerControl();
    }
}
