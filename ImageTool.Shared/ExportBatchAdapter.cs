using System.Linq;
using ImageTool.Core;
using ImageTool.Imaging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Shared;

public class ExportBatchAdapter : IBatchCapable
{
    public const string Plugin = "Export";
    public const string OpExport = "Export";

    public string PluginId => Plugin;
    public IEnumerable<string> SupportedOpTypes => new[] { OpExport };

    private readonly IHistoryService? _history;
    private readonly EditOpRegistry _ops;
    private readonly EditPipeline _pipeline;
    private readonly ImageDecoderRegistry _decoders;

    public ExportBatchAdapter(IHistoryService? history = null)
    {
        _history = history;
        _ops = EditOpRegistry.CreateDefault();
        _pipeline = new EditPipeline(_ops);
        _decoders = ImageDecoderRegistry.CreateDefault();
    }

    public Task RunJobAsync(BatchJob job, IProgress<int> progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(5);

            string format = job.Params.GetValueOrDefault("format", "png").ToLowerInvariant();
            string outDir = job.Params.GetValueOrDefault("outDir", "");
            string pattern = job.Params.GetValueOrDefault("pattern", "{name}.{ext}");
            int maxLong = int.TryParse(job.Params.GetValueOrDefault("maxLongEdge", "0"), out var ml) ? ml : 0;
            int resizePct = int.TryParse(job.Params.GetValueOrDefault("resizePercent", "0"), out var rp) ? rp : 0;
            string watermark = job.Params.GetValueOrDefault("watermark", "");
            bool writeXmp = string.Equals(job.Params.GetValueOrDefault("writeXmp", "false"), "true", StringComparison.OrdinalIgnoreCase);
            // Sharpen-for-output (9.4): "none" | "screen" | "low" | "high". Áp SAU resize.
            string outputSharpen = job.Params.GetValueOrDefault("outputSharpen", "none").ToLowerInvariant();
            // Bảo toàn EXIF gốc (camera/lens/ISO/ngày/GPS) trên file xuất (mặc định bật).
            bool copyExif = !string.Equals(job.Params.GetValueOrDefault("copyExif", "true"), "false", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            Directory.CreateDirectory(outDir);

            // Nếu ảnh có chỉnh sửa non-destructive -> render full-res qua pipeline (bake edits).
            Image image = LoadBaked(job.InputPath, ct);
            progress.Report(30);

            // Resize theo % (ưu tiên) hoặc cạnh dài tối đa.
            if (resizePct > 0 && resizePct != 100)
            {
                int nw = Math.Max(1, image.Width * resizePct / 100);
                int nh = Math.Max(1, image.Height * resizePct / 100);
                image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(nw, nh), Sampler = KnownResamplers.Lanczos3 }));
            }
            else if (maxLong > 0 && Math.Max(image.Width, image.Height) > maxLong)
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

            // Watermark văn bản (góc phải-dưới).
            if (!string.IsNullOrWhiteSpace(watermark))
                TryDrawWatermark(image, watermark);

            // Sharpen-for-output (9.4): bù độ mềm do resampling. Áp sau resize, trước encode.
            ApplyOutputSharpen(image, outputSharpen);

            progress.Report(60);
            ct.ThrowIfCancellationRequested();

            string baseName = Path.GetFileNameWithoutExtension(job.InputPath);
            string ext = format switch
            {
                "jpg" or "jpeg" => "jpg",
                "webp" => "webp",
                "tif" or "tiff" => "tiff",
                _ => "png"
            };
            string fileName = FileNameTokenizer.Resolve(pattern, new FileNameTokenizer.Context
            {
                OriginalName = baseName,
                Extension = ext,
                Width = image.Width,
                Height = image.Height,
                ParentFolder = new DirectoryInfo(Path.GetDirectoryName(job.InputPath) ?? ".").Name,
                Now = DateTime.Now,
            });
            // legacy token {id} (job id) vẫn hỗ trợ.
            fileName = fileName.Replace("{id}", job.Id);
            // token {size} cho xuất đa kích thước (giá trị long-edge px của bản này).
            if (job.Params.TryGetValue("sizeToken", out var szTok) && !string.IsNullOrEmpty(szTok))
                fileName = fileName.Replace("{size}", szTok);
            if (!fileName.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase)) fileName += "." + ext;
            string outPath = Path.Combine(outDir, fileName);
            // Không ghi đè im lặng: nếu file đích đã có, thêm hậu tố " (1)"... (trừ khi overwrite=true).
            bool overwrite = string.Equals(job.Params.GetValueOrDefault("overwrite", "false"), "true", StringComparison.OrdinalIgnoreCase);
            if (!overwrite)
                outPath = FileNameTokenizer.EnsureUniquePath(outPath);

            // Bảo toàn EXIF gốc (đã dọn orientation/kích thước cũ) trên ảnh xuất.
            if (copyExif)
                ExifWriter.PreserveExif(job.InputPath, image);

            // Nén sâu: dựng encoder từ params (Squoosh-style), hỗ trợ dung lượng mục tiêu (target KB).
            long targetBytes = long.TryParse(job.Params.GetValueOrDefault("targetKB", "0"), out var tkb) && tkb > 0
                ? tkb * 1024L : 0L;
            var result = TargetSizeEncoder.Encode(image, format, job.Params, targetBytes);
            File.WriteAllBytes(outPath, result.Data);
            image.Dispose();

            // XMP sidecar (tùy chọn) cạnh file gốc.
            if (writeXmp && _history != null)
            {
                try
                {
                    var stack = _history.GetStack(job.InputPath);
                    XmpSidecar.Write(job.InputPath, stack, _history.GetPointer(job.InputPath));
                }
                catch (Exception ex) { AppLog.Warn("Export.Xmp", $"không ghi được sidecar: {ex.Message}"); }
            }

            job.OutputPath = outPath;
            progress.Report(100);
        }, ct);
    }

    /// <summary>
    /// Sharpen-for-output: bù độ mềm do resampling khi xuất. Mức:
    ///   screen ~ nhẹ (web/màn hình), low ~ in giấy thường, high ~ in matte/độ phân giải cao.
    /// Dùng unsharp mask (GaussianSharpen) của ImageSharp với sigma theo mức.
    /// </summary>
    private static void ApplyOutputSharpen(Image image, string level)
    {
        float sigma = level switch
        {
            "screen" => 0.6f,
            "low" => 0.9f,
            "high" => 1.4f,
            _ => 0f
        };
        if (sigma <= 0f) return;
        try { image.Mutate(x => x.GaussianSharpen(sigma)); }
        catch { /* processor không khả dụng -> bỏ qua, không chặn export */ }
    }

    /// <summary>Vẽ watermark chữ ở góc phải-dưới. Bỏ qua nếu font không khả dụng.</summary>
    private static void TryDrawWatermark(Image image, string text)
    {
        try
        {
            if (!SystemFonts.Families.Any()) return;
            var family = SystemFonts.Families.First();
            float size = Math.Max(12f, image.Width * 0.025f);
            var font = family.CreateFont(size, FontStyle.Bold);
            var opts = new TextOptions(font);
            var rect = TextMeasurer.MeasureSize(text, opts);
            float x = image.Width - rect.Width - size * 0.6f;
            float y = image.Height - rect.Height - size * 0.6f;
            image.Mutate(ctx => ctx.DrawText(text, font,
                Color.FromRgba(255, 255, 255, 180), new PointF(x, y)));
        }
        catch (Exception ex) { AppLog.Warn("Export.Watermark", $"bỏ qua watermark: {ex.Message}"); }
    }

    /// <summary>Load ảnh; nếu có history active và decode được -> bake edits ở full-res.</summary>
    private Image LoadBaked(string inputPath, CancellationToken ct)
    {
        try
        {
            if (_history != null && _decoders.CanDecode(inputPath))
            {
                var stack = _history.GetStack(inputPath);
                int pointer = _history.GetPointer(inputPath);
                if (pointer > 0 && stack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var decoded = _decoders.Decode(inputPath);
                    var rendered = _pipeline.Render(decoded.Image, stack, pointer);
                    return ImageEncoder.ToRgba32(rendered);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AppLog.Error("Export.LoadBaked", $"không bake được {inputPath}, dùng ảnh gốc", ex); }
        return Image.Load(inputPath);
    }
}
