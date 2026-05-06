using System;
using System.Windows.Controls;
using ImageTool.Core;

namespace ImageTool.Plugins.VisionTagger;

public class VisionTaggerPlugin : IImagePlugin
{
    private UserControl? _uiComponent;

    public string Name => "Auto Tagger";
    public string Version => "1.0.0";
    public string Description => "Tự động phân tích, tạo mô tả và gán thẻ (Tags) cho hình ảnh sử dụng AI.";

    public void Initialize(IServiceProvider serviceProvider)
    {
        // Khởi tạo các service cần thiết hoặc cấu trúc model ở đây
        // (Tuỳ thuộc vào việc sử dụng ONNX, Python Worker, hay Cloud API)
    }

    public object GetUIComponent()
    {
        _uiComponent ??= new VisionTaggerControl();
        return _uiComponent;
    }
}
