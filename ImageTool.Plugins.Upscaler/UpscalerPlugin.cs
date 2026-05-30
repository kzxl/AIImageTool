using System;
using System.IO;
using ImageTool.Core;
using ImageTool.Imaging;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Plugins.Upscaler;

public class UpscalerPlugin : IImagePlugin
{
    public string Name => "AI Upscaler";
    public string Version => "1.0.0";
    public string Description => "Upscale images using ONNX Real-ESRGAN or similar model.";

    private IServiceProvider _serviceProvider = null!;
    private UpscalerControl _uiComponent = null!;

    public void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _uiComponent = new UpscalerControl();

        // Đăng ký batch capability vào BatchService nếu có
        var batch = serviceProvider.GetService<IBatchService>();
        batch?.RegisterCapability(new UpscalerBatchAdapter());

        // Đăng ký delegate AI upscale vào pipeline (#7): AiUpscaleOp gọi qua đây khi export.
        AiOpHost.UpscaleProcessor = (linear, factor) => UpscaleLinear(linear, factor);

        // Pass services xuống UI control để gọi IBatchService + IWorkspaceService
        _uiComponent.AttachServices(serviceProvider);
    }

    /// <summary>Phóng to LinearImage bằng OnnxUpscaler: linear -> Rgba32 -> Process -> linear.</summary>
    private static LinearImage UpscaleLinear(LinearImage linear, int factor)
    {
        string? mdPath = FindModel();
        if (mdPath == null) return linear; // không có model -> giữ nguyên

        using var input = ToRgba32(linear);
        // targetMp lớn để Process upscale theo model; factor điều khiển model 2x/4x đã cố định.
        long targetMp = (long)input.Width * input.Height * factor * factor / 1_000_000 + 1;
        var up = new OnnxUpscaler(mdPath, -1, PerformanceMode.Safe);
        using var result = up.Process(input, null, (int)Math.Clamp(targetMp, 1, 200));
        return FromRgba32(result);
    }

    private static string? FindModel()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var p in new[]
        {
            Path.Combine(baseDir, "Plugins", "ImageTool.Plugins.Upscaler", "Models"),
            Path.Combine(baseDir, "Models"),
        })
        {
            if (!Directory.Exists(p)) continue;
            var files = Directory.GetFiles(p, "*.onnx");
            if (files.Length > 0) return files[0];
        }
        return null;
    }

    private static Image<Rgba32> ToRgba32(LinearImage img)
    {
        var outImg = new Image<Rgba32>(img.Width, img.Height);
        float[] src = img.Pixels;
        outImg.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                int baseOff = y * img.Width * 4;
                for (int x = 0; x < row.Length; x++)
                {
                    int o = baseOff + x * 4;
                    row[x] = new Rgba32(
                        ColorSpace.EncodeByte(src[o]), ColorSpace.EncodeByte(src[o + 1]),
                        ColorSpace.EncodeByte(src[o + 2]),
                        (byte)Math.Clamp(src[o + 3] * 255f, 0, 255));
                }
            }
        });
        return outImg;
    }

    private static LinearImage FromRgba32(Image<Rgba32> img)
    {
        var linear = new LinearImage(img.Width, img.Height);
        float[] dst = linear.Pixels;
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < acc.Height; y++)
            {
                var row = acc.GetRowSpan(y);
                int baseOff = y * img.Width * 4;
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 p = ref row[x];
                    int o = baseOff + x * 4;
                    dst[o] = ColorSpace.DecodeByte(p.R);
                    dst[o + 1] = ColorSpace.DecodeByte(p.G);
                    dst[o + 2] = ColorSpace.DecodeByte(p.B);
                    dst[o + 3] = p.A / 255f;
                }
            }
        });
        return linear;
    }

    public object GetUIComponent()
    {
        return _uiComponent;
    }
}
