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

    /// <summary>Tạo style từ danh sách operations cho sẵn (vd import từ Lightroom XMP).</summary>
    Style SaveFromOperations(string name, IEnumerable<EditOperation> operations, string? description = null);

    /// <summary>Apply style: copy operations vào history stack của ảnh đích.</summary>
    void ApplyToImage(Style style, string imagePath);

    /// <summary>
    /// Apply style theo module (D6.2): append = true giữ edit hiện có, chỉ thay/thêm module style; false
    /// thay toàn bộ. moduleKeys = null -> mọi module có trong style. Chỉ tác động op Develop.
    /// </summary>
    void ApplyToImageMerged(Style style, string imagePath, bool append, ISet<string>? moduleKeys = null);

    void Delete(string styleId);
    void Rename(string styleId, string newName);

    event EventHandler? StylesChanged;
}
