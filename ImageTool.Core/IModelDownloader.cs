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

    /// <summary>U²-Net (salient object / subject segmentation) — sinh mask Subject. ~168MB.</summary>
    public static readonly ModelDescriptor U2Net = new()
    {
        Id = "u2net",
        FileName = "u2net.onnx",
        Url = "https://huggingface.co/tomjackson2023/rembg/resolve/main/u2net.onnx?download=true",
        ExpectedSize = 0,
        Description = "U^2-Net salient object segmentation (Subject mask)"
    };

    /// <summary>SCUNet denoiser (color, real noise). Dùng cho AI Denoise op cuối chuỗi.</summary>
    public static readonly ModelDescriptor ScuNetColor = new()
    {
        Id = "scunet-color",
        FileName = "scunet_color_real_psnr.onnx",
        Url = "https://huggingface.co/Phips/SCUNet_onnx/resolve/main/scunet_color_real_psnr.onnx?download=true",
        ExpectedSize = 0,
        Description = "SCUNet real-image color denoiser"
    };
}

