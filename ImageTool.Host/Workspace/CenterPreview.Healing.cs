using System;
using System.Windows;
using System.Windows.Input;

namespace ImageTool.Host.Workspace;

// Healing brush click capture (#6). Bật từ DevelopPanel; click trên ảnh -> AddHealSpot.
public partial class CenterPreview
{
    private bool _healMode;
    private DevelopPanel? _healPanel;

    /// <summary>Liên kết DevelopPanel để nhận tín hiệu bật/tắt heal + trả điểm click.</summary>
    public void BindHealingPanel(DevelopPanel panel)
    {
        _healPanel = panel;
        panel.HealingModeChanged += (_, on) =>
        {
            _healMode = on;
            if (on)
            {
                // tắt các overlay khác để tránh tranh chấp click.
                if (_cropMode) ToggleCropMode();
                SetMode(LighttableMode.Single);
                ResetZoom();
                paneSingle.Cursor = Cursors.Cross;
            }
            else
            {
                paneSingle.Cursor = Cursors.Arrow;
            }
        };
    }

    /// <summary>Gọi từ PaneSingle_MouseDown khi đang heal mode. Trả true nếu đã xử lý.</summary>
    private bool TryHandleHealClick(MouseButtonEventArgs e)
    {
        if (!_healMode || _healPanel == null) return false;
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0 || img.Height <= 0) return true;
        var p = e.GetPosition(paneSingle);
        if (p.X < img.Left || p.X > img.Right || p.Y < img.Top || p.Y > img.Bottom) return true;
        float nx = (float)((p.X - img.Left) / img.Width);
        float ny = (float)((p.Y - img.Top) / img.Height);
        _healPanel.AddHealSpot(nx, ny);
        return true;
    }
}
