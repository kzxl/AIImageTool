using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host;

/// <summary>
/// Điều phối các tính năng AI ảnh (6.6 segmentation + 4.3 denoise). Tải model lazy qua
/// IModelDownloader, khởi tạo session ONNX khi cần lần đầu, và đăng ký delegate denoise vào
/// <see cref="AiOpHost"/> để pipeline gọi được mà không phụ thuộc ONNX.
///
/// Mọi inference chạy nền; lỗi (thiếu model/GPU) ghi AppLog, không làm sập app.
/// </summary>
public sealed class AiMaskService : IDisposable
{
    private readonly IModelDownloader _downloader;
    private OnnxSegmenter? _segmenter;
    private OnnxDenoiser? _denoiser;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>Thư mục lưu mask PNG sinh ra (cache, cạnh catalog).</summary>
    private readonly string _maskDir;

    public AiMaskService(IModelDownloader downloader)
    {
        _downloader = downloader;
        _maskDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImageTool", "aimasks");
        Directory.CreateDirectory(_maskDir);

        // Đăng ký denoise delegate: lần đầu gọi sẽ khởi tạo session (đồng bộ trong pipeline thread).
        AiOpHost.DenoiseProcessor = (img, strength, scale) =>
        {
            try
            {
                EnsureDenoiser();
                _denoiser?.Apply(img, strength, scale);
            }
            catch (Exception ex) { ImageTool.Shared.AppLog.Error("AiMaskService.Denoise", "inference lỗi", ex); }
        };
    }

    /// <summary>
    /// Sinh mask chủ thể cho ảnh; trả đường dẫn PNG mask (để gắn vào RasterMask). Tải model nếu cần.
    /// Chạy nền — gọi await. Null nếu lỗi.
    /// </summary>
    public async Task<string?> GenerateSubjectMaskAsync(string imagePath, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            // Cache (#2): nếu mask cho ảnh này (path+mtime) đã tồn tại -> tái dùng, khỏi chạy ONNX.
            string maskPath = Path.Combine(_maskDir, MaskName(imagePath));
            if (File.Exists(maskPath) && new FileInfo(maskPath).Length > 0)
                return maskPath;

            var modelPath = await _downloader.EnsureAsync(KnownModels.U2Net, progress, ct);
            await _initLock.WaitAsync(ct);
            try { _segmenter ??= new OnnxSegmenter(modelPath); }
            finally { _initLock.Release(); }

            await Task.Run(() => _segmenter!.GenerateMask(imagePath, maskPath), ct);
            return File.Exists(maskPath) ? maskPath : null;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            ImageTool.Shared.AppLog.Error("AiMaskService.Subject", imagePath, ex);
            return null;
        }
    }

    /// <summary>Đảm bảo model denoise tải + session sẵn sàng (đồng bộ, dùng trong delegate).</summary>
    private void EnsureDenoiser()
    {
        if (_denoiser != null) return;
        _initLock.Wait();
        try
        {
            if (_denoiser != null) return;
            var path = _downloader.EnsureAsync(KnownModels.ScuNetColor).GetAwaiter().GetResult();
            _denoiser = new OnnxDenoiser(path);
        }
        finally { _initLock.Release(); }
    }

    private static string MaskName(string imagePath)
    {
        var fi = new FileInfo(imagePath);
        long ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;
        string key = $"{imagePath.ToLowerInvariant()}|{ticks}";
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).Substring(0, 16) + "_subject.png";
    }

    public void Dispose()
    {
        AiOpHost.DenoiseProcessor = null;
        _segmenter?.Dispose();
        _denoiser?.Dispose();
        _initLock.Dispose();
    }
}
