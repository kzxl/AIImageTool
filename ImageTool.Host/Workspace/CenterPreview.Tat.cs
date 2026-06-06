using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

public partial class CenterPreview
{
    private bool _tatMode;
    private string _tatSubMode = "sat"; // sat, lum, hue
    private DevelopPanel? _tatPanel;

    private bool _isDraggingTat;
    private Point _tatStartMouse;
    private float[]? _tatWeights;
    private float[]? _tatStartHue;
    private float[]? _tatStartSat;
    private float[]? _tatStartLum;

    private static readonly float[] TatBandCenters = { 0f, 30f, 60f, 120f, 180f, 240f, 280f, 320f };

    public void BindTat(DevelopPanel panel)
    {
        _tatPanel = panel;
        panel.TatStateChanged += (s, e) =>
        {
            _tatMode = e.Active;
            _tatSubMode = e.Mode;
            if (_tatMode)
            {
                SetMode(LighttableMode.Single);
                paneSingle.Cursor = Cursors.Cross;
            }
            else
            {
                paneSingle.Cursor = Cursors.Arrow;
            }
        };
    }

    private bool TryHandleTatMouseDown(MouseButtonEventArgs e)
    {
        if (!_tatMode || _tatPanel == null) return false;
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0 || img.Height <= 0) return false;

        var p = e.GetPosition(paneSingle);
        if (p.X < img.Left || p.X > img.Right || p.Y < img.Top || p.Y > img.Bottom) return false;

        var colorOpt = GetPixelColorAt(p);
        if (colorOpt == null) return false;

        var color = colorOpt.Value;
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;

        RgbToHsv(r, g, b, out float h, out float s, out float v);

        // Tính trọng số 8 dải
        _tatWeights = new float[HslMixerOp.Bands];
        float wSum = 0f;
        for (int i = 0; i < HslMixerOp.Bands; i++)
        {
            float w = TatBandWeight(h, TatBandCenters[i]);
            _tatWeights[i] = w;
            wSum += w;
        }
        // Chuẩn hóa trọng số
        if (wSum > 1e-6f)
        {
            float inv = 1f / wSum;
            for (int i = 0; i < HslMixerOp.Bands; i++) _tatWeights[i] *= inv;
        }

        // Lấy HSL hiện tại
        _tatPanel.GetHslValues(out var startHue, out var startSat, out var startLum);
        _tatStartHue = startHue;
        _tatStartSat = startSat;
        _tatStartLum = startLum;

        _isDraggingTat = true;
        _tatStartMouse = p;
        paneSingle.CaptureMouse();

        e.Handled = true;
        return true;
    }

    private void TryHandleTatMouseMove(MouseEventArgs e)
    {
        if (!_isDraggingTat || _tatPanel == null || _tatWeights == null || _tatStartHue == null || _tatStartSat == null || _tatStartLum == null) return;

        var p = e.GetPosition(paneSingle);
        double dy = _tatStartMouse.Y - p.Y; // Kéo lên = dương (tăng), kéo xuống = âm (giảm)
        float delta = (float)(dy / 250.0); // Kéo 250 pixel để tăng/giảm tối đa 1.0

        var newHue = _tatStartHue.ToArray();
        var newSat = _tatStartSat.ToArray();
        var newLum = _tatStartLum.ToArray();

        for (int i = 0; i < HslMixerOp.Bands; i++)
        {
            float w = _tatWeights[i];
            if (w <= 0f) continue;

            if (_tatSubMode == "sat")
            {
                newSat[i] = Math.Clamp(_tatStartSat[i] + delta * w, -1f, 1f);
            }
            else if (_tatSubMode == "lum")
            {
                newLum[i] = Math.Clamp(_tatStartLum[i] + delta * w, -1f, 1f);
            }
            else if (_tatSubMode == "hue")
            {
                newHue[i] = Math.Clamp(_tatStartHue[i] + delta * w, -1f, 1f);
            }
        }

        _tatPanel.UpdateHslValues(newHue, newSat, newLum, schedule: true);
        e.Handled = true;
    }

    private void TryHandleTatMouseUp(MouseButtonEventArgs e)
    {
        if (!_isDraggingTat || _tatPanel == null || _tatWeights == null || _tatStartHue == null || _tatStartSat == null || _tatStartLum == null) return;

        _isDraggingTat = false;
        paneSingle.ReleaseMouseCapture();

        var p = e.GetPosition(paneSingle);
        double dy = _tatStartMouse.Y - p.Y;
        float delta = (float)(dy / 250.0);

        var newHue = _tatStartHue.ToArray();
        var newSat = _tatStartSat.ToArray();
        var newLum = _tatStartLum.ToArray();

        for (int i = 0; i < HslMixerOp.Bands; i++)
        {
            float w = _tatWeights[i];
            if (w <= 0f) continue;

            if (_tatSubMode == "sat")
            {
                newSat[i] = Math.Clamp(_tatStartSat[i] + delta * w, -1f, 1f);
            }
            else if (_tatSubMode == "lum")
            {
                newLum[i] = Math.Clamp(_tatStartLum[i] + delta * w, -1f, 1f);
            }
            else if (_tatSubMode == "hue")
            {
                newHue[i] = Math.Clamp(_tatStartHue[i] + delta * w, -1f, 1f);
            }
        }

        _tatPanel.UpdateHslValues(newHue, newSat, newLum, schedule: false); // Commit cuối cùng
        e.Handled = true;
    }

    private Color? GetPixelColorAt(Point uiPoint)
    {
        if (imgPreview.Source is not BitmapSource bs) return null;
        var img = GetDisplayedImageRect();
        if (img.IsEmpty || img.Width <= 0 || img.Height <= 0) return null;

        double nx = (uiPoint.X - img.Left) / img.Width;
        double ny = (uiPoint.Y - img.Top) / img.Height;

        int px = (int)Math.Clamp(nx * bs.PixelWidth, 0, bs.PixelWidth - 1);
        int py = (int)Math.Clamp(ny * bs.PixelHeight, 0, bs.PixelHeight - 1);

        try
        {
            byte[] pixel = new byte[4];
            int stride = (bs.Format.BitsPerPixel + 7) / 8;

            if (bs.Format == PixelFormats.Indexed1 || bs.Format == PixelFormats.Indexed2 || 
                bs.Format == PixelFormats.Indexed4 || bs.Format == PixelFormats.Indexed8)
            {
                return null;
            }

            bs.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, stride, 0);

            byte r = 0, g = 0, b = 0;
            if (bs.Format == PixelFormats.Bgr32 || bs.Format == PixelFormats.Bgra32)
            {
                b = pixel[0]; g = pixel[1]; r = pixel[2];
            }
            else if (bs.Format == PixelFormats.Rgb24)
            {
                r = pixel[0]; g = pixel[1]; b = pixel[2];
            }
            else if (bs.Format == PixelFormats.Rgb48)
            {
                r = pixel[1]; g = pixel[3]; b = pixel[5];
            }
            else
            {
                if (stride >= 3)
                {
                    r = pixel[2]; g = pixel[1]; b = pixel[0];
                }
                else
                {
                    r = pixel[0]; g = pixel[0]; b = pixel[0];
                }
            }
            return Color.FromRgb(r, g, b);
        }
        catch
        {
            return null;
        }
    }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        v = max;
        float delta = max - min;
        s = max > 1e-6f ? delta / max : 0f;
        if (delta < 1e-6f) { h = 0f; return; }
        if (max == r) h = 60f * (((g - b) / delta) % 6f);
        else if (max == g) h = 60f * (((b - r) / delta) + 2f);
        else h = 60f * (((r - g) / delta) + 4f);
        if (h < 0f) h += 360f;
    }

    private static float TatBandWeight(float hue, float center)
    {
        float d = Math.Abs(hue - center);
        if (d > 180f) d = 360f - d;
        const float window = 45f;
        if (d >= window) return 0f;
        return 0.5f * (1f + (float)Math.Cos(Math.PI * d / window));
    }
}
