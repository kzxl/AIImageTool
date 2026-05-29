namespace ImageTool.Core;

public interface IImagePlugin
{
    string Name { get; }
    string Version { get; }
    string Description { get; }

    /// <summary>
    /// Lifecycle: Initialize plugin resources
    /// </summary>
    void Initialize(IServiceProvider serviceProvider);

    /// <summary>
    /// Get the main UI component (usually a WPF UserControl).
    /// V2 plugins (workspace-integrated) trả về panel chỉ chứa controls — không tự
    /// hiển thị preview ảnh. Preview do host điều khiển qua IImageToolHost.
    /// </summary>
    object GetUIComponent();
}

/// <summary>
/// Host API mà workspace cấp cho plugin để hiển thị preview/result thay vì plugin tự render.
/// Plugin nhận instance này qua IServiceProvider trong Initialize().
/// </summary>
public interface IImageToolHost
{
    /// <summary>Đường dẫn ảnh đang active trong workspace; null nếu chưa chọn.</summary>
    string? ActiveImagePath { get; }

    /// <summary>Bắn event khi user chọn ảnh khác trong workspace.</summary>
    event EventHandler<string?>? ActiveImageChanged;

    /// <summary>
    /// Hiện kết quả "after" trên CenterPreview (Single mode) cùng splitter so sánh.
    /// resultPath là file đã lưu (nếu plugin save) hoặc null nếu chỉ in-memory bitmap.
    /// </summary>
    void ShowResult(string? resultPath, byte[]? imageBytes = null);

    /// <summary>
    /// Đóng kết quả "after", trở về preview gốc.
    /// </summary>
    void ClearResult();

    /// <summary>
    /// Cập nhật progress hiển thị trên status bar workspace (0..100, -1 = ẩn).
    /// </summary>
    void ReportProgress(int percent, string? status = null);
}
