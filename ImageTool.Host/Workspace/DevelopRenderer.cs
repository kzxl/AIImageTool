using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

/// <summary>
/// Động cơ render preview phi phá hủy cho Develop. Quy trình:
///   decode file -> LinearImage gốc (cache, bất biến) -> proxy thu nhỏ cho realtime ->
///   replay chuỗi EditOperation qua EditPipeline -> BitmapSource (WriteableBitmap) cho UI.
///
/// Full-res chỉ dựng khi export. Mọi render chạy off-UI-thread; job cũ bị hủy khi có
/// yêu cầu mới (CancellationToken) để slider mượt.
/// </summary>
public sealed class DevelopRenderer
{
    private readonly EditOpRegistry _ops;
    private readonly EditPipeline _pipeline;
    private readonly CachedEditPipeline _cachedPipeline;
    private readonly ImageDecoderRegistry _decoders;

    // Cache ảnh gốc + proxy theo path (bất biến). Giới hạn nhỏ để khỏi phình RAM.
    private readonly object _cacheLock = new();
    private string? _cachedPath;
    private LinearImage? _cachedProxy;   // proxy linear (đã thu nhỏ)
    private float _cachedScale = 1f;     // proxyW / fullW
    private int _cachedFullW, _cachedFullH;

    private CancellationTokenSource? _cts;

    /// <summary>Cạnh dài tối đa của proxy preview (px). Đủ nét cho màn hình, đủ nhanh để kéo slider.</summary>
    public int ProxyLongEdge { get; set; } = 2048;

    public DevelopRenderer()
    {
        _ops = EditOpRegistry.CreateDefault();
        _pipeline = new EditPipeline(_ops);
        _cachedPipeline = new CachedEditPipeline(_ops);
        _decoders = ImageDecoderRegistry.CreateDefault();
    }

    public EditOpRegistry Ops => _ops;
    public ImageDecoderRegistry Decoders => _decoders;

    public bool CanDecode(string path) => _decoders.CanDecode(path);

    /// <summary>
    /// Render preview cho ảnh + chuỗi op (tới pointer). Trả BitmapSource đã Freeze (an toàn cross-thread),
    /// hoặc null nếu bị hủy / lỗi. Tự cache proxy theo path.
    /// </summary>
    public async Task<BitmapSource?> RenderPreviewAsync(
        string path, IReadOnlyList<EditOperation> ops, int pointer)
    {
        // Hủy job render trước đó.
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        // Snapshot ops (list có thể đổi ở thread khác).
        var opsCopy = new List<EditOperation>(ops);
        int count = Math.Clamp(pointer, 0, opsCopy.Count);

        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var proxy = GetOrBuildProxy(path, token);
                if (proxy == null) return null;
                token.ThrowIfCancellationRequested();

                // Cache theo tầng (10.6): replay chỉ từ op bị đổi. Cùng path -> tái dùng snapshot.
                var rendered = _cachedPipeline.RenderScaled(path, proxy, opsCopy, _cachedScale, count);
                token.ThrowIfCancellationRequested();

                return ToBitmapSource(rendered);
            }, token);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.RenderPreview", path, ex); return null; }
    }

    /// <summary>Render full-res cho export (đồng bộ, không cache proxy). Trả LinearImage kết quả.</summary>
    public LinearImage RenderFullRes(string path, IReadOnlyList<EditOperation> ops, int pointer)
    {
        var decoded = _decoders.Decode(path);
        var baseImg = decoded.Image;
        int count = Math.Clamp(pointer, 0, ops.Count);
        return _pipeline.Render(baseImg, ops, count);
    }

    private LinearImage? GetOrBuildProxy(string path, CancellationToken token)
    {
        lock (_cacheLock)
        {
            if (string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) && _cachedProxy != null)
                return _cachedProxy;
        }

        if (!_decoders.CanDecode(path)) return null;
        var decoded = _decoders.Decode(path);
        token.ThrowIfCancellationRequested();
        var full = decoded.Image;

        int longEdge = Math.Max(full.Width, full.Height);
        float scale = longEdge > ProxyLongEdge ? (float)ProxyLongEdge / longEdge : 1f;
        LinearImage proxy = scale < 1f ? Downscale(full, scale) : full;

        lock (_cacheLock)
        {
            _cachedPath = path;
            _cachedProxy = proxy;
            _cachedScale = scale;
            _cachedFullW = full.Width;
            _cachedFullH = full.Height;
        }
        return proxy;
    }

    /// <summary>Thu nhỏ ảnh linear bằng box-average (chạy ở linear nên không bị tối như resize gamma).</summary>
    private static LinearImage Downscale(LinearImage src, float scale)
    {
        int nw = Math.Max(1, (int)MathF.Round(src.Width * scale));
        int nh = Math.Max(1, (int)MathF.Round(src.Height * scale));
        var dst = new LinearImage(nw, nh);
        float[] s = src.Pixels, d = dst.Pixels;
        int sw = src.Width, sh = src.Height;
        float xr = (float)sw / nw, yr = (float)sh / nh;

        Parallel.For(0, nh, ny =>
        {
            int sy0 = (int)(ny * yr);
            int sy1 = Math.Min(sh, (int)((ny + 1) * yr));
            if (sy1 <= sy0) sy1 = sy0 + 1;
            for (int nx = 0; nx < nw; nx++)
            {
                int sx0 = (int)(nx * xr);
                int sx1 = Math.Min(sw, (int)((nx + 1) * xr));
                if (sx1 <= sx0) sx1 = sx0 + 1;
                float r = 0, g = 0, b = 0, a = 0;
                int n = 0;
                for (int yy = sy0; yy < sy1; yy++)
                {
                    int row = yy * sw * 4;
                    for (int xx = sx0; xx < sx1; xx++)
                    {
                        int o = row + xx * 4;
                        r += s[o]; g += s[o + 1]; b += s[o + 2]; a += s[o + 3];
                        n++;
                    }
                }
                float inv = n > 0 ? 1f / n : 0f;
                int do_ = (ny * nw + nx) * 4;
                d[do_] = r * inv; d[do_ + 1] = g * inv; d[do_ + 2] = b * inv; d[do_ + 3] = a * inv;
            }
        });
        return dst;
    }

    /// <summary>LinearImage -> BGRA32 WriteableBitmap (sRGB encode tại chỗ). Freeze để dùng cross-thread.</summary>
    public static BitmapSource ToBitmapSource(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        int stride = w * 4;
        var buffer = new byte[stride * h];
        float[] src = img.Pixels;

        Parallel.For(0, h, y =>
        {
            int rowF = y * w * 4;
            int rowB = y * stride;
            for (int x = 0; x < w; x++)
            {
                int o = rowF + x * 4;
                int bo = rowB + x * 4;
                byte rr = ColorSpace.EncodeByteFast(src[o]);
                byte gg = ColorSpace.EncodeByteFast(src[o + 1]);
                byte bb = ColorSpace.EncodeByteFast(src[o + 2]);
                float af = src[o + 3];
                byte aa = (byte)(af <= 0f ? 0 : (af >= 1f ? 255 : (int)(af * 255f + 0.5f)));
                // BGRA order
                buffer[bo] = bb; buffer[bo + 1] = gg; buffer[bo + 2] = rr; buffer[bo + 3] = aa;
            }
        });

        wb.WritePixels(new Int32Rect(0, 0, w, h), buffer, stride, 0);
        wb.Freeze();
        return wb;
    }

    /// <summary>Xoá cache (gọi khi đổi ảnh để giải phóng RAM nếu cần).</summary>
    public void InvalidateCache(string? path = null)
    {
        lock (_cacheLock)
        {
            if (path == null || string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _cachedPath = null;
                _cachedProxy = null;
                _cachedPipeline.Invalidate();
            }
        }
    }

    /// <summary>Phân tích Auto Tone trên proxy đã cache (hoặc decode nếu cần). Null nếu không decode được.</summary>
    public AutoTone.Suggestion? AnalyzeAuto(string path)
    {
        try
        {
            LinearImage? proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            return AutoTone.Analyze(proxy);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.AnalyzeAuto", path, ex); return null; }
    }

    /// <summary>Ước lượng góc nghiêng (auto-straighten) trên proxy. Trả góc độ; null nếu lỗi.</summary>
    public float? AnalyzeStraightenAngle(string path)
    {
        try
        {
            LinearImage? proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            return AutoStraighten.EstimateAngle(proxy);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.AutoStraighten", path, ex); return null; }
    }

    /// <summary>Phân tích Auto Levels (D2.5) trên proxy. Trả black/white/gamma; null nếu lỗi.</summary>
    public AutoTone.LevelsSuggestion? AnalyzeAutoLevels(string path)
    {
        try
        {
            LinearImage? proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            return AutoTone.AnalyzeLevels(proxy);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.AnalyzeAutoLevels", path, ex); return null; }
    }

    /// <summary>Phân tích Auto Color (per-channel levels) trên proxy. Trả black/white mỗi kênh; null nếu lỗi.</summary>
    public AutoTone.ColorLevelsSuggestion? AnalyzeAutoColor(string path)
    {
        try
        {
            LinearImage? proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            return AutoTone.AnalyzeColorLevels(proxy);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.AnalyzeAutoColor", path, ex); return null; }
    }

    /// <summary>Phân tích Auto White Balance trên proxy. Trả gain per-channel; null nếu lỗi.</summary>
    public AutoWhiteBalance.Gains? AnalyzeAutoWhiteBalance(string path)
    {
        try
        {
            LinearImage? proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            return AutoWhiteBalance.Analyze(proxy, AutoWhiteBalance.Strategy.GrayWorld);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.AutoWB", path, ex); return null; }
    }

    /// <summary>
    /// Eyedropper WB (3.1): lấy mẫu vùng quanh điểm chuẩn hoá (nx,ny) trên proxy gốc rồi tính gain
    /// để pixel đó thành xám trung tính. Lấy trung bình ô 5x5 cho ổn định. Null nếu lỗi/quá tối.
    /// </summary>
    public AutoWhiteBalance.Gains? SampleWhiteBalance(string path, float nx, float ny)
    {
        try
        {
            var proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            int w = proxy.Width, h = proxy.Height;
            int cx = Math.Clamp((int)(nx * (w - 1)), 0, w - 1);
            int cy = Math.Clamp((int)(ny * (h - 1)), 0, h - 1);
            float[] px = proxy.Pixels;

            double sr = 0, sg = 0, sb = 0; int n = 0;
            for (int dy = -2; dy <= 2; dy++)
            {
                int y = cy + dy; if (y < 0 || y >= h) continue;
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx; if (x < 0 || x >= w) continue;
                    int o = (y * w + x) * 4;
                    sr += px[o]; sg += px[o + 1]; sb += px[o + 2]; n++;
                }
            }
            if (n == 0) return null;
            float ar = (float)(sr / n), ag = (float)(sg / n), ab = (float)(sb / n);
            float lum = ColorSpace.Luminance(ar, ag, ab);
            if (lum < 1e-4f) return null; // quá tối -> không tin cậy

            // gain để cân bằng về xám (chuẩn hoá theo G).
            float gray = (ar + ag + ab) / 3f;
            float gr = ar > 1e-6f ? gray / ar : 1f;
            float gg = ag > 1e-6f ? gray / ag : 1f;
            float gb = ab > 1e-6f ? gray / ab : 1f;
            if (gg < 1e-6f) gg = 1f;
            return new AutoWhiteBalance.Gains { R = gr / gg, G = 1f, B = gb / gg };
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.SampleWB", path, ex); return null; }
    }

    /// <summary>
    /// Lấy mẫu film base (linear) tại điểm chuẩn hoá trên proxy GỐC (chưa áp op) — dùng cho eyedropper
    /// Film Negative: click vào mép phim trống (sáng nhất) để lấy màu base. Null nếu không decode được.
    /// </summary>
    public (float R, float G, float B)? SampleFilmBase(string path, float nx, float ny)
    {
        try
        {
            var proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            return FilmNegativeOp.SampleBase(proxy, nx, ny, 3);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.SampleFilmBase", path, ex); return null; }
    }

    /// <summary>
    /// Tính histogram + clip warning cho ảnh đã áp ops (trên proxy, downscale thêm cho nhanh).
    /// Dùng cho histogram live trong DevelopPanel (11.3). Null nếu không decode được.
    /// </summary>
    public HistogramData? ComputeHistogram(string path, IReadOnlyList<EditOperation> ops, int pointer)
    {
        try
        {
            var proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            // downscale mạnh để histogram tính nhanh (đủ chính xác cho phân phối).
            int longEdge = Math.Max(proxy.Width, proxy.Height);
            float histScale = longEdge > 512 ? 512f / longEdge : 1f;
            var small = histScale < 1f ? Downscale(proxy, histScale) : proxy;
            int count = Math.Clamp(pointer, 0, ops.Count);
            // Dùng pipeline thường (không cache) để khỏi thrash cache theo tầng của preview chính.
            var rendered = _pipeline.RenderScaled(small, ops, _cachedScale * histScale, count);
            return HistogramData.Compute(rendered);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.Histogram", path, ex); return null; }
    }

    /// <summary>Tính waveform/RGB-parade cho ảnh đã áp ops (trên proxy downscale). Null nếu lỗi.</summary>
    public WaveformData? ComputeWaveform(string path, IReadOnlyList<EditOperation> ops, int pointer, int columns = 256)
    {
        try
        {
            var proxy = GetOrBuildProxy(path, CancellationToken.None);
            if (proxy == null) return null;
            int longEdge = Math.Max(proxy.Width, proxy.Height);
            float wfScale = longEdge > 512 ? 512f / longEdge : 1f;
            var small = wfScale < 1f ? Downscale(proxy, wfScale) : proxy;
            int count = Math.Clamp(pointer, 0, ops.Count);
            var rendered = _pipeline.RenderScaled(small, ops, _cachedScale * wfScale, count);
            return WaveformData.Compute(rendered, columns);
        }
        catch (Exception ex) { ImageTool.Shared.AppLog.Error("DevelopRenderer.Waveform", path, ex); return null; }
    }
}
