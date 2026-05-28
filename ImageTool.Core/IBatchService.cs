namespace ImageTool.Core;

public enum BatchJobStatus { Pending, Running, Completed, Failed, Canceled, Paused }

/// <summary>
/// 1 job = 1 ảnh + 1 plugin xử lý. Plugin nào hỗ trợ batch implements IBatchCapable.
/// </summary>
public class BatchJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public string PluginId { get; init; } = "";
    public string OpType { get; init; } = "";
    public string InputPath { get; init; } = "";
    public string DisplayName => System.IO.Path.GetFileName(InputPath);
    public Dictionary<string, string> Params { get; init; } = new();

    public BatchJobStatus Status { get; set; } = BatchJobStatus.Pending;
    public int Progress { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// Plugin có khả năng xử lý hàng loạt declare interface này.
/// PluginId/OpType lấy từ BatchJob để chọn handler.
/// </summary>
public interface IBatchCapable
{
    string PluginId { get; }
    /// <summary>Các OpType plugin xử lý được (vd: "Upscale", "Export").</summary>
    IEnumerable<string> SupportedOpTypes { get; }

    Task RunJobAsync(BatchJob job, IProgress<int> progress, CancellationToken ct);
}

public interface IBatchService
{
    IReadOnlyList<BatchJob> Jobs { get; }
    int MaxParallel { get; set; }
    bool IsPaused { get; }

    void Enqueue(BatchJob job);
    void EnqueueRange(IEnumerable<BatchJob> jobs);
    void RegisterCapability(IBatchCapable capable);

    void CancelJob(string jobId);
    void RetryJob(string jobId);
    void RemoveJob(string jobId);
    void Pause();
    void Resume();
    void ClearCompleted();

    event EventHandler<BatchJob>? JobUpdated;
    event EventHandler? QueueChanged;
}
