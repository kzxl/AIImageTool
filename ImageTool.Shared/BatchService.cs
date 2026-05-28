using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using ImageTool.Core;

namespace ImageTool.Shared;

public class BatchService : IBatchService, IDisposable
{
    private readonly ObservableCollection<BatchJob> _jobs = new();
    private readonly ConcurrentDictionary<string, IBatchCapable> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    private readonly SemaphoreSlim _slot;
    private readonly object _lock = new();
    private int _maxParallel = 1;
    private bool _paused;
    private readonly System.Threading.SynchronizationContext? _uiCtx;

    public BatchService()
    {
        _slot = new SemaphoreSlim(_maxParallel, 16);
        _uiCtx = System.Threading.SynchronizationContext.Current;
    }

    public IReadOnlyList<BatchJob> Jobs => _jobs;

    public int MaxParallel
    {
        get => _maxParallel;
        set
        {
            value = Math.Clamp(value, 1, 16);
            int delta = value - _maxParallel;
            _maxParallel = value;
            if (delta > 0) _slot.Release(delta);
            // Giảm: không thu hồi slot đang chạy, các slot mới release sẽ acquire ít hơn → tự cân bằng.
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsPaused => _paused;

    public event EventHandler<BatchJob>? JobUpdated;
    public event EventHandler? QueueChanged;

    public void RegisterCapability(IBatchCapable capable)
    {
        foreach (var op in capable.SupportedOpTypes)
        {
            _capabilities[Key(capable.PluginId, op)] = capable;
        }
    }

    public void Enqueue(BatchJob job)
    {
        Marshal(() => _jobs.Add(job));
        QueueChanged?.Invoke(this, EventArgs.Empty);
        _ = ScheduleAsync(job);
    }

    public void EnqueueRange(IEnumerable<BatchJob> jobs)
    {
        var list = jobs.ToList();
        Marshal(() => { foreach (var j in list) _jobs.Add(j); });
        QueueChanged?.Invoke(this, EventArgs.Empty);
        foreach (var j in list) _ = ScheduleAsync(j);
    }

    public void CancelJob(string jobId)
    {
        if (_running.TryGetValue(jobId, out var cts)) cts.Cancel();
        var job = _jobs.FirstOrDefault(j => j.Id == jobId);
        if (job != null && job.Status == BatchJobStatus.Pending)
        {
            job.Status = BatchJobStatus.Canceled;
            RaiseUpdate(job);
        }
    }

    public void RetryJob(string jobId)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == jobId);
        if (job == null) return;
        if (job.Status == BatchJobStatus.Completed || job.Status == BatchJobStatus.Running) return;
        job.Status = BatchJobStatus.Pending;
        job.Progress = 0;
        job.Error = null;
        RaiseUpdate(job);
        _ = ScheduleAsync(job);
    }

    public void RemoveJob(string jobId)
    {
        if (_running.TryGetValue(jobId, out var cts)) cts.Cancel();
        Marshal(() =>
        {
            var j = _jobs.FirstOrDefault(x => x.Id == jobId);
            if (j != null) _jobs.Remove(j);
        });
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause() { _paused = true; QueueChanged?.Invoke(this, EventArgs.Empty); }
    public void Resume() { _paused = false; QueueChanged?.Invoke(this, EventArgs.Empty); }

    public void ClearCompleted()
    {
        Marshal(() =>
        {
            var done = _jobs.Where(j => j.Status is BatchJobStatus.Completed or BatchJobStatus.Failed or BatchJobStatus.Canceled).ToList();
            foreach (var j in done) _jobs.Remove(j);
        });
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ScheduleAsync(BatchJob job)
    {
        await _slot.WaitAsync();
        try
        {
            // Đợi nếu đang pause
            while (_paused) await Task.Delay(200);

            if (job.Status == BatchJobStatus.Canceled) return;

            if (!_capabilities.TryGetValue(Key(job.PluginId, job.OpType), out var cap))
            {
                job.Status = BatchJobStatus.Failed;
                job.Error = $"No capability registered: {job.PluginId}/{job.OpType}";
                RaiseUpdate(job);
                return;
            }

            var cts = new CancellationTokenSource();
            _running[job.Id] = cts;
            try
            {
                job.Status = BatchJobStatus.Running;
                job.StartedAt = DateTime.UtcNow;
                RaiseUpdate(job);

                var prog = new Progress<int>(p =>
                {
                    job.Progress = p;
                    RaiseUpdate(job);
                });

                await cap.RunJobAsync(job, prog, cts.Token);

                if (cts.IsCancellationRequested)
                {
                    job.Status = BatchJobStatus.Canceled;
                }
                else
                {
                    job.Status = BatchJobStatus.Completed;
                    job.Progress = 100;
                }
            }
            catch (OperationCanceledException)
            {
                job.Status = BatchJobStatus.Canceled;
            }
            catch (Exception ex)
            {
                job.Status = BatchJobStatus.Failed;
                job.Error = ex.Message;
            }
            finally
            {
                job.FinishedAt = DateTime.UtcNow;
                _running.TryRemove(job.Id, out _);
                cts.Dispose();
                RaiseUpdate(job);
            }
        }
        finally
        {
            _slot.Release();
        }
    }

    private void RaiseUpdate(BatchJob job)
    {
        JobUpdated?.Invoke(this, job);
    }

    private void Marshal(Action a)
    {
        if (_uiCtx != null) _uiCtx.Send(_ => a(), null);
        else a();
    }

    private static string Key(string plugin, string op) => $"{plugin}|{op}";

    public void Dispose()
    {
        foreach (var cts in _running.Values) { try { cts.Cancel(); cts.Dispose(); } catch { } }
        _running.Clear();
        _slot.Dispose();
    }
}
