using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Adapter cho phép enqueue 1 BatchJob "ApplyStyle" cho mỗi ảnh selection.
/// Đơn giản: chỉ push history, chưa render output (rendering thuộc các plugin tương ứng).
/// </summary>
public class StyleBatchAdapter : IBatchCapable
{
    public const string Plugin = "Style";
    public const string OpApply = "ApplyStyle";

    private readonly IStyleService _styleService;

    public StyleBatchAdapter(IStyleService styleService)
    {
        _styleService = styleService;
    }

    public string PluginId => Plugin;
    public IEnumerable<string> SupportedOpTypes => new[] { OpApply };

    public Task RunJobAsync(BatchJob job, IProgress<int> progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(10);

            var styleId = job.Params.GetValueOrDefault("styleId", "");
            var style = _styleService.Styles.FirstOrDefault(s => s.Id == styleId);
            if (style == null) throw new InvalidOperationException($"Style {styleId} not found");

            progress.Report(40);
            _styleService.ApplyToImage(style, job.InputPath);

            // Note: side-effects (render output PNG, save) thuộc plugin xử lý op cụ thể.
            // Phase này chỉ ghi history; pipeline render sẽ build sau ở giai đoạn render unified.
            job.OutputPath = job.InputPath;
            progress.Report(100);
        }, ct);
    }
}
