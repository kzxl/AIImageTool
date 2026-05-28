namespace ImageTool.Core;

public interface IThumbnailService
{
    /// <summary>
    /// Lấy đường dẫn file thumbnail cache cho ảnh; nếu chưa có sẽ enqueue tạo nền và return null.
    /// </summary>
    string? TryGetThumbnailPath(string imagePath, int size = 256);

    /// <summary>
    /// Yêu cầu sinh thumbnail nền; raise event ThumbnailReady khi xong.
    /// </summary>
    void RequestThumbnail(string imagePath, int size = 256);

    event EventHandler<ThumbnailReadyEventArgs>? ThumbnailReady;
}

public class ThumbnailReadyEventArgs : EventArgs
{
    public string ImagePath { get; }
    public string ThumbnailPath { get; }
    public int Size { get; }
    public ThumbnailReadyEventArgs(string imagePath, string thumbnailPath, int size)
    {
        ImagePath = imagePath;
        ThumbnailPath = thumbnailPath;
        Size = size;
    }
}
