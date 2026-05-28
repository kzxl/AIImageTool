using System.Text.Json;
using ImageTool.Core;

namespace ImageTool.Shared;

public class StyleService : IStyleService
{
    private readonly string _file;
    private readonly IHistoryService _history;
    private readonly List<Style> _styles = new();

    public StyleService(IHistoryService history)
    {
        _history = history;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImageTool", "styles");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "styles.json");
        Load();
    }

    public IReadOnlyList<Style> Styles => _styles;
    public event EventHandler? StylesChanged;

    public Style SaveFromHistory(string name, string imagePath, string? description = null)
    {
        var stack = _history.GetStack(imagePath);
        var style = new Style
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Style {_styles.Count + 1}" : name,
            Description = description,
            // Clone operations để không reference shared
            Operations = stack.Select(o => new EditOperation
            {
                Id = Guid.NewGuid().ToString("N"),
                PluginId = o.PluginId,
                OpType = o.OpType,
                Title = o.Title,
                Timestamp = DateTime.UtcNow,
                Params = new Dictionary<string, string>(o.Params)
            }).ToList()
        };
        _styles.Add(style);
        Save();
        StylesChanged?.Invoke(this, EventArgs.Empty);
        return style;
    }

    public void ApplyToImage(Style style, string imagePath)
    {
        foreach (var op in style.Operations)
        {
            _history.Push(imagePath, new EditOperation
            {
                Id = Guid.NewGuid().ToString("N"),
                PluginId = op.PluginId,
                OpType = op.OpType,
                Title = op.Title,
                Timestamp = DateTime.UtcNow,
                Params = new Dictionary<string, string>(op.Params)
            });
        }
    }

    public void Delete(string styleId)
    {
        if (_styles.RemoveAll(s => s.Id == styleId) > 0)
        {
            Save();
            StylesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Rename(string styleId, string newName)
    {
        var s = _styles.FirstOrDefault(x => x.Id == styleId);
        if (s == null || string.IsNullOrWhiteSpace(newName)) return;
        s.Name = newName;
        Save();
        StylesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        if (!File.Exists(_file)) return;
        try
        {
            var json = File.ReadAllText(_file);
            var list = JsonSerializer.Deserialize<List<Style>>(json);
            if (list != null) _styles.AddRange(list);
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_styles, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_file)) File.Delete(_file);
            File.Move(tmp, _file);
        }
        catch { }
    }
}
