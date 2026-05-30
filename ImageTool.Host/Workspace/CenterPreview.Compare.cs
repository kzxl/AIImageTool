using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageTool.Core;

namespace ImageTool.Host.Workspace;

// Side-by-side before/after compare (11.4).
public partial class CenterPreview
{
    private bool _compareMode;
    private double _cmpZoom = 1.0;

    private void BtnCompare_Click(object sender, RoutedEventArgs e) => ToggleCompareMode();

    /// <summary>Zoom đồng bộ 2 khung compare (8.6) — cuộn chuột phóng cả before lẫn after cùng mức.</summary>
    private void PaneCompare_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (!_compareMode) return;
        double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        _cmpZoom = Math.Clamp(_cmpZoom * factor, 1.0, 8.0);
        cmpScaleBefore.ScaleX = cmpScaleBefore.ScaleY = _cmpZoom;
        cmpScaleAfter.ScaleX = cmpScaleAfter.ScaleY = _cmpZoom;
        e.Handled = true;
    }

    private void ResetCompareZoom()
    {
        _cmpZoom = 1.0;
        cmpScaleBefore.ScaleX = cmpScaleBefore.ScaleY = 1.0;
        cmpScaleAfter.ScaleX = cmpScaleAfter.ScaleY = 1.0;
        cmpPanBefore.X = cmpPanBefore.Y = 0;
        cmpPanAfter.X = cmpPanAfter.Y = 0;
    }

    private void ToggleCompareMode()
    {
        var path = _workspace?.ActiveImage;
        if (string.IsNullOrEmpty(path)) return;

        _compareMode = !_compareMode;
        if (_compareMode)
        {
            // tắt crop nếu đang bật để tránh tranh chấp overlay.
            if (_cropMode) ToggleCropMode();
            paneSingle.Visibility = Visibility.Collapsed;
            paneGrid.Visibility = Visibility.Collapsed;
            paneCull.Visibility = Visibility.Collapsed;
            paneFull.Visibility = Visibility.Collapsed;
            paneCompare.Visibility = Visibility.Visible;
            btnCompare.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x7E, 0xFF));
            ResetCompareZoom();
            _ = LoadCompareAsync(path);
        }
        else
        {
            paneCompare.Visibility = Visibility.Collapsed;
            imgCompareBefore.Source = null;
            imgCompareAfter.Source = null;
            btnCompare.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30));
            SwitchMode(LighttableMode.Single);
        }
    }

    /// <summary>Render ảnh gốc (pointer=0) và ảnh đã chỉnh (pointer hiện tại) vào 2 khung.</summary>
    private async System.Threading.Tasks.Task LoadCompareAsync(string path)
    {
        // AFTER: ảnh đã chỉnh. BEFORE: ảnh gốc.
        var ops = _history?.GetStack(path) ?? (IReadOnlyList<EditOperation>)Array.Empty<EditOperation>();
        int pointer = _history?.GetPointer(path) ?? 0;

        if (_renderer.CanDecode(path) && pointer > 0)
        {
            try
            {
                var before = await _renderer.RenderPreviewAsync(path, ops, 0);
                var after = await _renderer.RenderPreviewAsync(path, ops, pointer);
                if (!_compareMode) return;
                if (before != null) imgCompareBefore.Source = before;
                if (after != null) imgCompareAfter.Source = after;
                return;
            }
            catch { }
        }

        // Fallback: ảnh chưa chỉnh hoặc không decode được -> cả 2 khung là ảnh gốc trên đĩa.
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 1600;
            bmp.EndInit();
            bmp.Freeze();
            imgCompareBefore.Source = bmp;
            imgCompareAfter.Source = bmp;
        }
        catch { }
    }
}
