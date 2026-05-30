using System;
using System.Windows;
using System.Windows.Input;

namespace ImageTool.Host.Workspace;

// White Balance eyedropper (3.1): bật pick mode, click 1 điểm trên ảnh -> trả toạ độ chuẩn hoá.
public partial class CenterPreview
{
    private bool _wbPickMode;
    private bool _filmBasePickMode;

    /// <summary>Liên kết DevelopPanel để nhận yêu cầu eyedropper + trả mẫu về.</summary>
    public void BindWhiteBalancePick(DevelopPanel panel)
    {
        panel.WhiteBalancePickRequested += (_, _) =>
        {
            var path = _workspace?.ActiveImage;
            if (string.IsNullOrEmpty(path) || !_renderer.CanDecode(path)) return;
            _wbPickMode = true;
            _filmBasePickMode = false;
            SetMode(LighttableMode.Single);
            paneSingle.Cursor = Cursors.Cross;
        };
        panel.FilmBasePickRequested += (_, _) =>
        {
            var path = _workspace?.ActiveImage;
            if (string.IsNullOrEmpty(path) || !_renderer.CanDecode(path)) return;
            _filmBasePickMode = true;
            _wbPickMode = false;
            SetMode(LighttableMode.Single);
            paneSingle.Cursor = Cursors.Cross;
        };
        // Khi click trong pane single ở pick mode -> lấy mẫu rồi tắt.
        _wbPickPanel = panel;
    }

    private DevelopPanel? _wbPickPanel;

    /// <summary>Gọi từ PaneSingle_MouseDown khi đang ở pick mode. Trả true nếu đã xử lý click.</summary>
    private bool TryHandleWbPick(MouseButtonEventArgs e)
    {
        if (!_wbPickMode && !_filmBasePickMode) return false;
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0 || img.Height <= 0) { CancelWbPick(); return true; }
        var p = e.GetPosition(paneSingle);
        if (p.X < img.Left || p.X > img.Right || p.Y < img.Top || p.Y > img.Bottom) { CancelWbPick(); return true; }
        float nx = (float)((p.X - img.Left) / img.Width);
        float ny = (float)((p.Y - img.Top) / img.Height);
        if (_filmBasePickMode) _wbPickPanel?.ApplyFilmBasePick(nx, ny);
        else _wbPickPanel?.ApplyWhiteBalancePick(nx, ny);
        CancelWbPick();
        return true;
    }

    private void CancelWbPick()
    {
        _wbPickMode = false;
        _filmBasePickMode = false;
        paneSingle.Cursor = Cursors.Arrow;
    }
}
