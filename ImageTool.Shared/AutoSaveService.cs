using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Auto-save (#12): lắng nghe <see cref="IHistoryService.HistoryChanged"/> và ghi sidecar .xmp cho ảnh
/// bị đổi sau 1 khoảng DEBOUNCE (mặc định 2s) — tránh ghi đĩa liên tục khi kéo slider. Giúp khôi phục
/// chỉnh sửa nếu app tắt đột ngột (XmpSidecar được đọc lại khi mở ảnh). Tự dọn timer khi Dispose.
///
/// Phần lập lịch debounce tách khỏi I/O (qua delegate writer) để unit test bằng writer giả + đồng hồ ảo.
/// </summary>
public sealed class AutoSaveService : IDisposable
{
    private readonly IHistoryService _history;
    private readonly Action<string, IReadOnlyList<EditOperation>, int> _writer;
    private readonly int _debounceMs;
    private readonly ConcurrentDictionary<string, Timer> _timers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AutoSaveService(IHistoryService history,
        Action<string, IReadOnlyList<EditOperation>, int>? writer = null, int debounceMs = 2000)
    {
        _history = history;
        _writer = writer ?? ((path, ops, ptr) => XmpSidecar.Write(path, ops, ptr));
        _debounceMs = debounceMs;
        _history.HistoryChanged += OnHistoryChanged;
    }

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e)
    {
        if (_disposed || string.IsNullOrEmpty(e.ImagePath)) return;
        Schedule(e.ImagePath, e.Stack, e.Pointer);
    }

    /// <summary>Lên lịch ghi cho 1 ảnh (reset debounce nếu đang chờ).</summary>
    public void Schedule(string imagePath, IReadOnlyList<EditOperation> ops, int pointer)
    {
        if (_disposed) return;
        // Luôn lưu snapshot MỚI NHẤT vào _pending; timer chỉ kích hoạt Flush (đọc _pending).
        _pending[imagePath] = (new List<EditOperation>(ops), pointer);

        _timers.AddOrUpdate(imagePath,
            _ => new Timer(_ => Flush(imagePath), null, _debounceMs, Timeout.Infinite),
            (_, existing) => { existing.Change(_debounceMs, Timeout.Infinite); return existing; });
    }

    private readonly ConcurrentDictionary<string, (List<EditOperation> Ops, int Ptr)> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    private void Flush(string imagePath)
    {
        if (_disposed) return;
        if (!_pending.TryRemove(imagePath, out var latest)) return;
        try { _writer(imagePath, latest.Ops, latest.Ptr); }
        catch (Exception ex) { AppLog.Warn("AutoSaveService", $"{imagePath}: {ex.Message}"); }
        finally
        {
            if (_timers.TryRemove(imagePath, out var t)) t.Dispose();
        }
    }

    /// <summary>Ghi ngay toàn bộ các ảnh đang chờ (gọi khi đóng app để không mất chỉnh sửa).</summary>
    public void FlushAll()
    {
        foreach (var kv in _pending)
        {
            try { _writer(kv.Key, kv.Value.Ops, kv.Value.Ptr); }
            catch (Exception ex) { AppLog.Warn("AutoSaveService.FlushAll", $"{kv.Key}: {ex.Message}"); }
        }
        _pending.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _history.HistoryChanged -= OnHistoryChanged;
        FlushAll();
        foreach (var t in _timers.Values) t.Dispose();
        _timers.Clear();
    }
}
