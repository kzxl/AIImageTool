namespace ImageTool.Core;

public class Style
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<EditOperation> Operations { get; set; } = new();
}

public interface IStyleService
{
    IReadOnlyList<Style> Styles { get; }

    /// <summary>Snapshot history hiện tại của ảnh thành 1 style mới.</summary>
    Style SaveFromHistory(string name, string imagePath, string? description = null);

    /// <summary>Apply style: copy operations vào history stack của ảnh đích.</summary>
    void ApplyToImage(Style style, string imagePath);

    void Delete(string styleId);
    void Rename(string styleId, string newName);

    event EventHandler? StylesChanged;
}
