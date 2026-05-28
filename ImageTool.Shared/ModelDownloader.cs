using System.Net.Http;
using ImageTool.Core;

namespace ImageTool.Shared;

public class ModelDownloader : IModelDownloader
{
    private readonly string _cacheDir;
    private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

    public ModelDownloader()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageTool", "models");
        Directory.CreateDirectory(_cacheDir);
    }

    public string GetPath(ModelDescriptor desc) => Path.Combine(_cacheDir, desc.FileName);

    public bool IsCached(ModelDescriptor desc)
    {
        var p = GetPath(desc);
        if (!File.Exists(p)) return false;
        if (desc.ExpectedSize > 0)
        {
            try { return new FileInfo(p).Length == desc.ExpectedSize; } catch { return false; }
        }
        return true;
    }

    public async Task<string> EnsureAsync(ModelDescriptor desc, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var dest = GetPath(desc);
        if (IsCached(desc)) return dest;

        var tmp = dest + ".part";
        if (File.Exists(tmp)) File.Delete(tmp);

        using var resp = await _http.GetAsync(desc.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        long? total = resp.Content.Headers.ContentLength;
        long received = 0;
        var buf = new byte[81920];

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                received += n;
                if (progress != null)
                {
                    double pct = total.HasValue && total.Value > 0
                        ? (double)received / total.Value * 100.0 : 0;
                    progress.Report(new DownloadProgress(received, total, pct));
                }
            }
        }

        if (File.Exists(dest)) File.Delete(dest);
        File.Move(tmp, dest);
        return dest;
    }
}

