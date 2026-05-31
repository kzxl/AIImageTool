using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using ImageTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Shared;

public class ThumbnailService : IThumbnailService, IDisposable
{
    private readonly string _cacheDir;
    private readonly Channel<ThumbnailRequest> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();
    private readonly Task[] _workers;

    public event EventHandler<ThumbnailReadyEventArgs>? ThumbnailReady;

    public ThumbnailService(int workerCount = 4)
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageTool", "thumbs");
        Directory.CreateDirectory(_cacheDir);

        _queue = Channel.CreateUnbounded<ThumbnailRequest>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        _workers = new Task[Math.Max(1, workerCount)];
        for (int i = 0; i < _workers.Length; i++)
        {
            _workers[i] = Task.Run(() => WorkerLoopAsync(_cts.Token));
        }
    }

    public string? TryGetThumbnailPath(string imagePath, int size = 256)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;
        var thumbPath = GetThumbPath(imagePath, size);
        if (File.Exists(thumbPath)) return thumbPath;
        RequestThumbnail(imagePath, size);
        return null;
    }

    public void RequestThumbnail(string imagePath, int size = 256)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return;
        var key = imagePath + "|" + size;
        if (!_inFlight.TryAdd(key, 0)) return;
        _queue.Writer.TryWrite(new ThumbnailRequest(imagePath, size, key));
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var req in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    if (!File.Exists(req.ImagePath))
                    {
                        _inFlight.TryRemove(req.Key, out _);
                        continue;
                    }

                    var thumbPath = GetThumbPath(req.ImagePath, req.Size);
                    if (File.Exists(thumbPath))
                    {
                        _inFlight.TryRemove(req.Key, out _);
                        ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(req.ImagePath, thumbPath, req.Size));
                        continue;
                    }

                    GenerateThumbnail(req.ImagePath, thumbPath, req.Size);
                    _inFlight.TryRemove(req.Key, out _);
                    ThumbnailReady?.Invoke(this, new ThumbnailReadyEventArgs(req.ImagePath, thumbPath, req.Size));
                }
                catch
                {
                    _inFlight.TryRemove(req.Key, out _);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static void GenerateThumbnail(string srcPath, string dstPath, int size)
    {
        // TargetSize hint cho ImageSharp decoder để bỏ qua MIPS/scanline thừa khi có thể.
        // Với JPEG progressive/baseline, decoder chỉ decode subsample đủ để fit target.
        var decoderOpts = new SixLabors.ImageSharp.Formats.DecoderOptions
        {
            TargetSize = new Size(size, size)
        };

        // RAW: ImageSharp không đọc được -> trích JPEG preview nhúng trước.
        Image image;
        if (ImageTool.Imaging.RawPreviewExtractor.IsRawExtension(srcPath))
        {
            var jpeg = ImageTool.Imaging.RawPreviewExtractor.ExtractLargestJpeg(srcPath);
            if (jpeg == null) throw new NotSupportedException($"RAW không có JPEG preview: {Path.GetFileName(srcPath)}");
            using var ms = new MemoryStream(jpeg);
            image = Image.Load(decoderOpts, ms);
        }
        else
        {
            image = Image.Load(decoderOpts, srcPath);
        }

        using (image)
        {
            // Áp EXIF orientation vào pixel (ảnh chụp dọc không bị nằm ngang trong thumbnail).
            try { image.Mutate(x => x.AutoOrient()); } catch { }

            // Sau khi decode, có thể vẫn lớn hơn size → resize chính xác.
            if (image.Width > size || image.Height > size)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(size, size),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Bicubic
                }));
            }

            var dir = Path.GetDirectoryName(dstPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = dstPath + ".tmp";
            using (var fs = File.Create(tmp))
            {
                image.SaveAsJpeg(fs, new JpegEncoder { Quality = 82 });
            }
            if (File.Exists(dstPath)) File.Delete(dstPath);
            File.Move(tmp, dstPath);
        }
    }

    private string GetThumbPath(string imagePath, int size)
    {
        var fi = new FileInfo(imagePath);
        var hash = Sha1Hex(ComposeCacheKey(imagePath, fi.LastWriteTimeUtc.Ticks, fi.Length, size));
        var sub = hash.Substring(0, 2);
        return Path.Combine(_cacheDir, sub, hash + ".jpg");
    }

    /// <summary>
    /// Khoá cache thumbnail: gồm path (lowercase) + mtime + dung lượng + kích thước đích. Nhờ có mtime
    /// và dung lượng, file thay đổi -> khoá đổi -> tự sinh lại thumbnail (không phục vụ ảnh cũ). Tách
    /// thuần để unit test logic invalidate.
    /// </summary>
    public static string ComposeCacheKey(string imagePath, long mtimeTicks, long length, int size)
        => $"{imagePath.ToLowerInvariant()}|{mtimeTicks}|{length}|{size}";

    private static string Sha1Hex(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.Writer.TryComplete();
        try { Task.WaitAll(_workers, 2000); } catch { }
        _cts.Dispose();
    }

    private record ThumbnailRequest(string ImagePath, int Size, string Key);
}
