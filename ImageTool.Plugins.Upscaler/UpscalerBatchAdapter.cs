using ImageTool.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Plugins.Upscaler;

public class UpscalerBatchAdapter : IBatchCapable
{
    public const string Plugin = "Upscaler";
    public const string OpUpscale = "Upscale";

    public string PluginId => Plugin;
    public IEnumerable<string> SupportedOpTypes => new[] { OpUpscale };

    public Task RunJobAsync(BatchJob job, IProgress<int> progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            string modelFile = job.Params.GetValueOrDefault("model", "");
            int deviceId = int.TryParse(job.Params.GetValueOrDefault("device", "-1"), out var d) ? d : -1;
            int targetMp = int.TryParse(job.Params.GetValueOrDefault("targetMp", "24"), out var mp) ? mp : 24;
            var perfMode = job.Params.GetValueOrDefault("perf", "Safe") == "Unleashed"
                ? PerformanceMode.Unleashed : PerformanceMode.Safe;

            if (string.IsNullOrEmpty(modelFile))
            {
                // Fallback: Lanczos resize
                RunLanczos(job, targetMp, progress, ct);
                return;
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var mdPath = ModelLocator.Resolve(modelFile)
                ?? throw new System.IO.FileNotFoundException($"Model not found: {modelFile}");

            using var image = Image.Load<Rgba32>(job.InputPath);
            var upscaler = new OnnxUpscaler(mdPath, deviceId, perfMode);
            using var result = upscaler.Process(image, progress, targetMp, ct);

            ct.ThrowIfCancellationRequested();

            var outDir = System.IO.Path.Combine(baseDir, "Output");
            System.IO.Directory.CreateDirectory(outDir);
            string baseName = System.IO.Path.GetFileNameWithoutExtension(job.InputPath);
            string outPath = System.IO.Path.Combine(outDir, $"{baseName}_{targetMp}MP_{job.Id}.png");
            result.SaveAsPng(outPath);

            job.OutputPath = outPath;
            progress.Report(100);
        }, ct);
    }

    private static void RunLanczos(BatchJob job, int targetMp, IProgress<int> progress, CancellationToken ct)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        using var image = Image.Load(job.InputPath);
        progress.Report(20);

        long curr = (long)image.Width * image.Height;
        long target = targetMp * 1_000_000L;
        if (curr < target)
        {
            double sf = Math.Sqrt((double)target / curr);
            int nw = (int)(image.Width * sf), nh = (int)(image.Height * sf);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(nw, nh),
                Sampler = KnownResamplers.Lanczos3
            }));
            progress.Report(70);
        }
        ct.ThrowIfCancellationRequested();

        var outDir = System.IO.Path.Combine(baseDir, "Output");
        System.IO.Directory.CreateDirectory(outDir);
        string baseName = System.IO.Path.GetFileNameWithoutExtension(job.InputPath);
        string outPath = System.IO.Path.Combine(outDir, $"{baseName}_{targetMp}MP_Lanczos_{job.Id}.png");
        image.SaveAsPng(outPath);
        job.OutputPath = outPath;
        progress.Report(100);
    }
}
