using System.Text.Json;
using ImageTool.Core;

namespace ImageTool.Shared;

public class SettingsService : ISettingsService
{
    private readonly string _file;
    private AppSettings _current;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImageTool");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "settings.json");

        _current = Load();
    }

    public AppSettings Current => _current;

    public event EventHandler? Changed;

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_file)) File.Delete(_file);
            File.Move(tmp, _file);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    public void AddRecentFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _current.RecentFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _current.RecentFolders.Insert(0, path);
        if (_current.RecentFolders.Count > 12)
            _current.RecentFolders.RemoveRange(12, _current.RecentFolders.Count - 12);
        _current.LastFolder = path;
        Save();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_file)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }
}
