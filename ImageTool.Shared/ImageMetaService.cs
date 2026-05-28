using System.Collections.Concurrent;
using System.Text.Json;
using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Lưu meta (rating/label/pick/tags) vào file .imgtool.json trong cùng folder ảnh.
/// 1 file/folder để giảm lượng IO và spam folder.
/// </summary>
public class ImageMetaService : IImageMetaService
{
    private const string SidecarFileName = ".imgtool.json";

    private readonly ConcurrentDictionary<string, FolderMeta> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _saveLock = new();

    public event EventHandler<ImageMetaChangedEventArgs>? MetaChanged;

    public ImageMeta Get(string imagePath)
    {
        var folder = GetFolderMeta(imagePath);
        var key = Path.GetFileName(imagePath);
        return folder.Items.TryGetValue(key, out var m) ? m : new ImageMeta();
    }

    public void SetRating(string imagePath, int rating)
    {
        Mutate(imagePath, m => m.Rating = Math.Clamp(rating, 0, 5));
    }

    public void SetLabel(string imagePath, ColorLabel label)
    {
        Mutate(imagePath, m => m.Label = label);
    }

    public void SetPick(string imagePath, PickFlag pick)
    {
        Mutate(imagePath, m => m.Pick = pick);
    }

    public void SetTags(string imagePath, IEnumerable<string> tags)
    {
        Mutate(imagePath, m => m.Tags = tags.Distinct().ToList());
    }

    public void SetDescription(string imagePath, string? description)
    {
        Mutate(imagePath, m => m.Description = description);
    }

    private void Mutate(string imagePath, Action<ImageMeta> apply)
    {
        var folder = GetFolderMeta(imagePath);
        var key = Path.GetFileName(imagePath);
        var meta = folder.Items.TryGetValue(key, out var existing) ? existing : new ImageMeta();
        apply(meta);
        folder.Items[key] = meta;
        SaveFolder(folder);
        MetaChanged?.Invoke(this, new ImageMetaChangedEventArgs(imagePath, meta));
    }

    private FolderMeta GetFolderMeta(string imagePath)
    {
        var dir = Path.GetDirectoryName(imagePath) ?? "";
        return _folders.GetOrAdd(dir, LoadFolder);
    }

    private static FolderMeta LoadFolder(string dir)
    {
        var fm = new FolderMeta { Folder = dir };
        var path = Path.Combine(dir, SidecarFileName);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, ImageMeta>>(json);
                if (dict != null)
                {
                    foreach (var kv in dict) fm.Items[kv.Key] = kv.Value;
                }
            }
            catch { }
        }
        return fm;
    }

    private void SaveFolder(FolderMeta fm)
    {
        lock (_saveLock)
        {
            try
            {
                var path = Path.Combine(fm.Folder, SidecarFileName);
                var json = JsonSerializer.Serialize(fm.Items, new JsonSerializerOptions { WriteIndented = false });
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); } catch { }
            }
            catch { }
        }
    }

    private class FolderMeta
    {
        public string Folder { get; set; } = "";
        public Dictionary<string, ImageMeta> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
