using ImageTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Shared;

public class ExportBatchAdapter : IBatchCapable
{
    public const string Plugin = "Export";
    public const string OpExport = "Export";

    public string PluginId => Plugin;
    public IEnumerable<string> SupportedOpTypes => new[] { OpExport };

    public Task RunJobAsync(BatchJob job, IProgress<int> progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(5);

            string format = job.Params.GetValueOrDefault("format", "png").ToLowerInvariant();
            int quality = int.TryParse(job.Params.GetValueOrDefault("quality", "90"), out var q) ? q : 90;
            string outDir = job.Params.GetValueOrDefault("outDir", "");
            string pattern = job.Params.GetValueOrDefault("pattern", "{name}.{ext}");
            int maxLong = int.TryParse(job.Params.GetValueOrDefault("maxLongEdge", "0"), out var ml) ? ml : 0;

            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            Directory.CreateDirectory(outDir);

            using var image = Image.Load(job.InputPath);
            progress.Report(30);

            if (maxLong > 0 && Math.Max(image.Width, image.Height) > maxLong)
            {
                double scale = (double)maxLong / Math.Max(image.Width, image.Height);
                int nw = (int)(image.Width * scale);
                int nh = (int)(image.Height * scale);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(nw, nh),
                    Sampler = KnownResamplers.Lanczos3
                }));
            }
            progress.Report(60);
            ct.ThrowIfCancellationRequested();

            string baseName = Path.GetFileNameWithoutExtension(job.InputPath);
            string ext = format switch { "jpg" or "jpeg" => "jpg", "webp" => "webp", _ => "png" };
            string fileName = pattern
                .Replace("{name}", baseName)
                .Replace("{ext}", ext)
                .Replace("{w}", image.Width.ToString())
                .Replace("{h}", image.Height.ToString())
                .Replace("{id}", job.Id);
            if (!fileName.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase)) fileName += "." + ext;
            string outPath = Path.Combine(outDir, fileName);

            switch (format)
            {
                case "jpg":
                case "jpeg":
                    image.SaveAsJpeg(outPath, new JpegEncoder { Quality = quality });
                    break;
                case "webp":
                    image.SaveAsWebp(outPath, new WebpEncoder { Quality = quality });
                    break;
                default:
                    image.SaveAsPng(outPath, new PngEncoder());
                    break;
            }

            job.OutputPath = outPath;
            progress.Report(100);
        }, ct);
    }
}
