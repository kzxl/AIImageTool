using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageTool.Host.Workspace;

// Clipping overlay (13.9): J phím bật/tắt. Đỏ = highlight cháy (>=250), xanh = shadow crushed (<=5).
public partial class CenterPreview
{
    private bool _clipOverlay;

    private void ToggleClipOverlay()
    {
        if (imgPreview.Source is not BitmapSource bs) return;
        _clipOverlay = !_clipOverlay;
        if (_clipOverlay)
        {
            imgClip.Source = BuildClipMask(bs);
            SyncClipTransform();
            imgClip.Visibility = Visibility.Visible;
        }
        else
        {
            imgClip.Visibility = Visibility.Collapsed;
            imgClip.Source = null;
        }
    }

    /// <summary>Cập nhật overlay khi ảnh preview đổi (nếu đang bật).</summary>
    private void RefreshClipOverlayIfActive()
    {
        if (!_clipOverlay) return;
        if (imgPreview.Source is BitmapSource bs)
        {
            imgClip.Source = BuildClipMask(bs);
            SyncClipTransform();
        }
    }

    /// <summary>Đồng bộ transform overlay với ảnh preview (zoom/pan).</summary>
    private void SyncClipTransform()
    {
        zoomScaleClip.ScaleX = zoomScale.ScaleX;
        zoomScaleClip.ScaleY = zoomScale.ScaleY;
        zoomPanClip.X = zoomPan.X;
        zoomPanClip.Y = zoomPan.Y;
    }

    /// <summary>
    /// Sinh ảnh mask trong suốt: pixel cháy sáng -> đỏ đặc, pixel mất chi tiết tối -> xanh đặc,
    /// còn lại trong suốt. Dùng để phủ lên preview căn theo cùng transform.
    /// </summary>
    private static BitmapSource BuildClipMask(BitmapSource src)
    {
        var fmt = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = fmt.PixelWidth, h = fmt.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        fmt.CopyPixels(pixels, stride, 0);

        const byte hi = 250, lo = 5;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            bool blown = r >= hi && g >= hi && b >= hi;
            bool crushed = r <= lo && g <= lo && b <= lo;
            if (blown) { pixels[i] = 60; pixels[i + 1] = 60; pixels[i + 2] = 255; pixels[i + 3] = 200; }
            else if (crushed) { pixels[i] = 255; pixels[i + 1] = 90; pixels[i + 2] = 60; pixels[i + 3] = 200; }
            else { pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = 0; }
        }

        var wb = new WriteableBitmap(w, h, src.DpiX, src.DpiY, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    /// <summary>Bật/tắt tạm thời chế độ clipping preview (phục vụ phím Alt khi kéo slider QoL).</summary>
    public void SetTemporaryClipOverlay(bool active)
    {
        if (imgPreview.Source is not BitmapSource bs) return;
        if (active)
        {
            imgClip.Source = BuildClipMask(bs);
            SyncClipTransform();
            imgClip.Visibility = Visibility.Visible;
        }
        else
        {
            if (!_clipOverlay) // Chỉ ẩn đi nếu người dùng không bật cứng bằng phím J
            {
                imgClip.Visibility = Visibility.Collapsed;
                imgClip.Source = null;
            }
        }
    }
}
