using System;
using ImageTool.Core;

namespace ImageTool.Plugins.MetaEditor;

public class MetaEditorPlugin : IImagePlugin
{
    public string Name => "Metadata Editor";
    public string Version => "1.0.0";
    public string Description => "View and edit image EXIF metadata easily without affecting pixel data.";

    private IServiceProvider _serviceProvider = null!;
    private MetaEditorControl _uiComponent = null!;

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _uiComponent = new MetaEditorControl();
        _uiComponent.AttachServices(serviceProvider);
    }

    public object GetUIComponent()
    {
        return _uiComponent;
    }
}
