namespace ImageTool.Core;

public class ModelDescriptor
{
    public string Id { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Url { get; init; } = "";
    public long ExpectedSize { get; init; }
    public string? Sha256 { get; init; }
    public string Description { get; init; } = "";
}

public interface IModelDownloader
{
    /// <summary>Trả path tuyệt đối tới file model. Tải nếu chưa có.</summary>
    Task<string> EnsureAsync(ModelDescriptor desc, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);

    /// <summary>True nếu file đã tồn tại với size hợp lệ.</summary>
    bool IsCached(ModelDescriptor desc);

    /// <summary>Path file model trong cache (có thể chưa tồn tại).</summary>
    string GetPath(ModelDescriptor desc);
}

public record DownloadProgress(long BytesReceived, long? TotalBytes, double Percent);

public static class KnownModels
{
    public static readonly ModelDescriptor WdViTV3 = new()
    {
        Id = "wd-vit-tagger-v3",
        FileName = "wd-vit-tagger-v3.onnx",
        Url = "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/model.onnx?download=true",
        ExpectedSize = 0,
        Description = "WD ViT Tagger v3 - multi-label image tagging"
    };

    public static readonly ModelDescriptor WdViTV3Tags = new()
    {
        Id = "wd-vit-tagger-v3-tags",
        FileName = "wd-vit-tagger-v3.csv",
        Url = "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/selected_tags.csv?download=true",
        ExpectedSize = 0,
        Description = "Tag list for WD ViT Tagger v3"
    };

    public static readonly ModelDescriptor GpenBfr512 = new()
    {
        Id = "gpen-bfr-512",
        FileName = "GPEN-BFR-512.onnx",
        Url = "https://huggingface.co/martintomov/comfy/resolve/main/facerestore_models/GPEN-BFR-512.onnx?download=true",
        ExpectedSize = 0,
        Description = "GPEN face restoration 512x512"
    };
}

