using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Lưu history vào file riêng .imgtool.history.json per folder, key = filename.
/// Tách khỏi meta để tránh ghi đè khi user chỉnh sao/label.
/// </summary>
public class HistoryService : IHistoryService
{
    private const string SidecarFileName = ".imgtool.history.json";

    private readonly ConcurrentDictionary<string, FolderHistory> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _saveLock = new();

    public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;

    public IReadOnlyList<EditOperation> GetStack(string imagePath)
    {
        var entry = GetEntry(imagePath);
        return entry.Stack.ToList();
    }

    public int GetPointer(string imagePath)
    {
        return GetEntry(imagePath).Pointer;
    }

    public void Push(string imagePath, EditOperation op)
    {
        var entry = GetEntry(imagePath);
        // Cắt redo phía sau pointer rồi append
        if (entry.Pointer < entry.Stack.Count)
            entry.Stack.RemoveRange(entry.Pointer, entry.Stack.Count - entry.Pointer);
        entry.Stack.Add(op);
        entry.Pointer = entry.Stack.Count;
        SaveAndRaise(imagePath, entry);
    }

    public void Upsert(string imagePath, EditOperation op)
    {
        var entry = GetEntry(imagePath);
        // Nếu op ngay trước pointer trùng loại + plugin => thay thế tại chỗ (live slider).
        if (entry.Pointer > 0)
        {
            var prev = entry.Stack[entry.Pointer - 1];
            if (string.Equals(prev.OpType, op.OpType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(prev.PluginId, op.PluginId, StringComparison.OrdinalIgnoreCase))
            {
                op.Id = prev.Id; // giữ Id để không phá liên kết
                entry.Stack[entry.Pointer - 1] = op;
                SaveAndRaise(imagePath, entry);
                return;
            }
        }
        // Khác loại: hành xử như Push (cắt redo + append).
        if (entry.Pointer < entry.Stack.Count)
            entry.Stack.RemoveRange(entry.Pointer, entry.Stack.Count - entry.Pointer);
        entry.Stack.Add(op);
        entry.Pointer = entry.Stack.Count;
        SaveAndRaise(imagePath, entry);
    }

    public void UpsertGroup(string imagePath, string pluginId, IReadOnlyList<EditOperation> ops)
    {
        var entry = GetEntry(imagePath);
        // Chỉ thao tác trong phạm vi đang active (bỏ redo phía sau).
        if (entry.Pointer < entry.Stack.Count)
            entry.Stack.RemoveRange(entry.Pointer, entry.Stack.Count - entry.Pointer);
        // Gỡ mọi op cùng plugin trong phạm vi active.
        entry.Stack.RemoveAll(o => string.Equals(o.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
        // Chèn lại nhóm theo thứ tự canonical ở cuối.
        entry.Stack.AddRange(ops);
        entry.Pointer = entry.Stack.Count;
        SaveAndRaise(imagePath, entry);
    }

    public EditOperation? Undo(string imagePath)
    {
        var entry = GetEntry(imagePath);
        if (entry.Pointer == 0) return null;
        entry.Pointer--;
        var op = entry.Stack[entry.Pointer];
        SaveAndRaise(imagePath, entry);
        return op;
    }

    public EditOperation? Redo(string imagePath)
    {
        var entry = GetEntry(imagePath);
        if (entry.Pointer >= entry.Stack.Count) return null;
        var op = entry.Stack[entry.Pointer];
        entry.Pointer++;
        SaveAndRaise(imagePath, entry);
        return op;
    }

    public void SetPointer(string imagePath, int pointer)
    {
        var entry = GetEntry(imagePath);
        entry.Pointer = Math.Clamp(pointer, 0, entry.Stack.Count);
        SaveAndRaise(imagePath, entry);
    }

    public void Clear(string imagePath)
    {
        var entry = GetEntry(imagePath);
        entry.Stack.Clear();
        entry.Pointer = 0;
        SaveAndRaise(imagePath, entry);
    }

    public void SaveSnapshot(string imagePath, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return;
        var entry = GetEntry(imagePath);
        // Chụp các op active (đến pointer), deep-clone để bất biến.
        var ops = entry.Stack.Take(entry.Pointer).Select(CloneOp).ToList();
        // Ghi đè nếu trùng tên (so sánh không phân biệt hoa thường).
        entry.Snapshots.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        entry.Snapshots.Add(new HistorySnapshot { Name = name, CreatedAt = DateTime.UtcNow, Ops = ops });
        SaveFolder(GetFolderHistory(imagePath));
        // Không đổi stack/pointer -> không cần raise render lại, nhưng raise để UI snapshot list cập nhật.
        HistoryChanged?.Invoke(this, new HistoryChangedEventArgs(imagePath, entry.Stack.ToList(), entry.Pointer));
    }

    public IReadOnlyList<HistorySnapshot> GetSnapshots(string imagePath)
        => GetEntry(imagePath).Snapshots.ToList();

    public bool ApplySnapshot(string imagePath, string name)
    {
        var entry = GetEntry(imagePath);
        var snap = entry.Snapshots.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (snap == null) return false;
        // Thay toàn bộ stack active bằng op snapshot (deep-clone), bỏ redo phía sau.
        entry.Stack.Clear();
        entry.Stack.AddRange(snap.Ops.Select(CloneOp));
        entry.Pointer = entry.Stack.Count;
        SaveAndRaise(imagePath, entry);
        return true;
    }

    public bool DeleteSnapshot(string imagePath, string name)
    {
        var entry = GetEntry(imagePath);
        int removed = entry.Snapshots.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;
        SaveFolder(GetFolderHistory(imagePath));
        HistoryChanged?.Invoke(this, new HistoryChangedEventArgs(imagePath, entry.Stack.ToList(), entry.Pointer));
        return true;
    }

    private static EditOperation CloneOp(EditOperation o) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        PluginId = o.PluginId,
        OpType = o.OpType,
        Title = o.Title,
        Timestamp = o.Timestamp,
        Params = new Dictionary<string, string>(o.Params),
    };

    private void SaveAndRaise(string imagePath, ImageHistoryEntry entry)
    {
        SaveFolder(GetFolderHistory(imagePath));
        HistoryChanged?.Invoke(this, new HistoryChangedEventArgs(imagePath, entry.Stack.ToList(), entry.Pointer));
    }

    private ImageHistoryEntry GetEntry(string imagePath)
    {
        var folder = GetFolderHistory(imagePath);
        var key = Path.GetFileName(imagePath);
        return folder.Items.GetOrAdd(key, _ => new ImageHistoryEntry());
    }

    private FolderHistory GetFolderHistory(string imagePath)
    {
        var dir = Path.GetDirectoryName(imagePath) ?? "";
        return _folders.GetOrAdd(dir, LoadFolder);
    }

    private static FolderHistory LoadFolder(string dir)
    {
        var fh = new FolderHistory { Folder = dir };
        var path = Path.Combine(dir, SidecarFileName);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, ImageHistoryEntry>>(json);
                if (dict != null)
                    foreach (var kv in dict) fh.Items[kv.Key] = kv.Value;
            }
            catch { }
        }
        return fh;
    }

    private void SaveFolder(FolderHistory fh)
    {
        lock (_saveLock)
        {
            try
            {
                var path = Path.Combine(fh.Folder, SidecarFileName);
                var json = JsonSerializer.Serialize(fh.Items);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); } catch { }
            }
            catch { }
        }
    }

    public class ImageHistoryEntry
    {
        public List<EditOperation> Stack { get; set; } = new();
        public int Pointer { get; set; }
        public List<HistorySnapshot> Snapshots { get; set; } = new();
    }

    private class FolderHistory
    {
        public string Folder { get; set; } = "";
        public ConcurrentDictionary<string, ImageHistoryEntry> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // ===== Virtual Copies =====
    public const string VirtualCopySuffix = "#vc";

    public string CreateVirtualCopy(string imagePath)
    {
        var entry = GetEntry(imagePath);
        var copies = GetVirtualCopies(imagePath);
        int nextNum = copies.Count + 1;
        string vcPath = $"{imagePath}{VirtualCopySuffix}{nextNum}";

        // Clone current stack
        var vcEntry = new ImageHistoryEntry
        {
            Pointer = entry.Pointer,
            Stack = entry.Stack.Select(op => new EditOperation
            {
                Id = Guid.NewGuid().ToString("N"),
                PluginId = op.PluginId,
                OpType = op.OpType,
                Title = op.Title,
                Timestamp = op.Timestamp,
                Params = new Dictionary<string, string>(op.Params)
            }).ToList(),
            Snapshots = entry.Snapshots.Select(s => new HistorySnapshot
            {
                Name = s.Name,
                CreatedAt = s.CreatedAt,
                Ops = s.Ops.Select(op => new EditOperation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PluginId = op.PluginId,
                    OpType = op.OpType,
                    Title = op.Title,
                    Timestamp = op.Timestamp,
                    Params = new Dictionary<string, string>(op.Params)
                }).ToList()
            }).ToList()
        };

        var fh = GetFolderHistory(imagePath);
        var key = Path.GetFileName(vcPath);
        fh.Items[key] = vcEntry;
        SaveFolder(fh);
        return vcPath;
    }

    public bool DeleteVirtualCopy(string virtualCopyPath)
    {
        if (!IsVirtualCopy(virtualCopyPath)) return false;
        var fh = GetFolderHistory(virtualCopyPath);
        var key = Path.GetFileName(virtualCopyPath);
        if (fh.Items.TryRemove(key, out _))
        {
            SaveFolder(fh);
            return true;
        }
        return false;
    }

    public IReadOnlyList<string> GetVirtualCopies(string imagePath)
    {
        var fh = GetFolderHistory(imagePath);
        var baseName = Path.GetFileName(imagePath);
        return fh.Items.Keys
            .Where(k => k.StartsWith(baseName + VirtualCopySuffix, StringComparison.OrdinalIgnoreCase))
            .Select(k => Path.Combine(Path.GetDirectoryName(imagePath) ?? "", k))
            .ToList();
    }

    public bool IsVirtualCopy(string path)
    {
        return path.Contains(VirtualCopySuffix, StringComparison.OrdinalIgnoreCase);
    }

    public string GetOriginalPath(string virtualCopyPath)
    {
        int idx = virtualCopyPath.IndexOf(VirtualCopySuffix, StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? virtualCopyPath[..idx] : virtualCopyPath;
    }
}
