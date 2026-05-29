namespace ImageTool.Core;

/// <summary>
/// 1 thao tác chỉnh sửa non-destructive. Plugin push instance vào IHistoryService.
/// Params dạng dictionary để serialize JSON dễ; plugin tự diễn giải khi replay.
/// </summary>
public class EditOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PluginId { get; set; } = "";   // ví dụ "ColorLab", "Upscaler"
    public string OpType { get; set; } = "";     // ví dụ "ApplyLUT", "WhiteBalance"
    public string Title { get; set; } = "";      // hiển thị trong UI history
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Params { get; set; } = new();
}

public interface IHistoryService
{
    /// <summary>Stack hiện tại của 1 ảnh (snapshot read-only).</summary>
    IReadOnlyList<EditOperation> GetStack(string imagePath);

    /// <summary>Push 1 op mới sau current pointer; cắt redo phía sau.</summary>
    void Push(string imagePath, EditOperation op);

    /// <summary>
    /// Cập nhật "live" 1 op: nếu op ngay trước pointer trùng OpType và PluginId thì thay thế
    /// tại chỗ (giữ Id, không tạo bước history mới) — dùng khi kéo slider. Ngược lại push op mới.
    /// </summary>
    void Upsert(string imagePath, EditOperation op);

    /// <summary>
    /// Quản lý 1 NHÓM op của cùng pluginId như trạng thái nguyên tử (vd panel Develop):
    /// trong phạm vi đang active, gỡ mọi op có PluginId == pluginId rồi chèn lại <paramref name="ops"/>
    /// theo đúng thứ tự cho trước (canonical) ở cuối phạm vi active. Cắt redo, đặt pointer = số op mới.
    /// Tránh nhân đôi op khi user xen kẽ chỉnh nhiều loại (Basic/HSL/Curve).
    /// </summary>
    void UpsertGroup(string imagePath, string pluginId, IReadOnlyList<EditOperation> ops);

    /// <summary>Undo: lùi pointer 1 bước. Trả op vừa undo (hoặc null nếu không có).</summary>
    EditOperation? Undo(string imagePath);

    /// <summary>Redo: tiến pointer 1 bước. Trả op vừa redo.</summary>
    EditOperation? Redo(string imagePath);

    /// <summary>Pointer hiện tại (số op active). 0 = base, len = full stack.</summary>
    int GetPointer(string imagePath);

    /// <summary>Set pointer đến vị trí cụ thể (0..len). UI gọi khi click step trong history.</summary>
    void SetPointer(string imagePath, int pointer);

    /// <summary>Xóa toàn bộ history của ảnh.</summary>
    void Clear(string imagePath);

    event EventHandler<HistoryChangedEventArgs>? HistoryChanged;
}

public class HistoryChangedEventArgs : EventArgs
{
    public string ImagePath { get; }
    public IReadOnlyList<EditOperation> Stack { get; }
    public int Pointer { get; }
    public HistoryChangedEventArgs(string imagePath, IReadOnlyList<EditOperation> stack, int pointer)
    {
        ImagePath = imagePath;
        Stack = stack;
        Pointer = pointer;
    }
}
