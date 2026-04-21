using System;
using ImageTool.Core;

namespace ImageTool.Plugins.MetaEditor;

public class MetaEditorPlugin : IImagePlugin
{
    public string Name => "Metadata Editor";
    public string Version => "1.0.0";
    public string Description => "View and edit image EXIF metadata easily without affecting pixel data.";

    private IServiceProvider _serviceProvider;
    private MetaEditorControl _uiComponent;

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _uiComponent = new MetaEditorControl();
    }

    public object GetUIComponent()
    {
        return _uiComponent;
    }
}
