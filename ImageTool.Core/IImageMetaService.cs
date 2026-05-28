namespace ImageTool.Core;

public enum ColorLabel
{
    None = 0,
    Red,
    Yellow,
    Green,
    Blue,
    Purple
}

public enum PickFlag
{
    None = 0,
    Pick = 1,
    Reject = -1
}

public class ImageMeta
{
    public int Rating { get; set; }      // 0..5
    public ColorLabel Label { get; set; }
    public PickFlag Pick { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Description { get; set; }
}

public interface IImageMetaService
{
    ImageMeta Get(string imagePath);
    void SetRating(string imagePath, int rating);
    void SetLabel(string imagePath, ColorLabel label);
    void SetPick(string imagePath, PickFlag pick);
    void SetTags(string imagePath, IEnumerable<string> tags);
    void SetDescription(string imagePath, string? description);

    event EventHandler<ImageMetaChangedEventArgs>? MetaChanged;
}

public class ImageMetaChangedEventArgs : EventArgs
{
    public string ImagePath { get; }
    public ImageMeta Meta { get; }
    public ImageMetaChangedEventArgs(string imagePath, ImageMeta meta)
    {
        ImagePath = imagePath;
        Meta = meta;
    }
}
