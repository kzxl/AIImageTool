using System.Collections.Concurrent;
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
    }

    private class FolderHistory
    {
        public string Folder { get; set; } = "";
        public ConcurrentDictionary<string, ImageHistoryEntry> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
