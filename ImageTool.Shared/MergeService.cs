using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageTool.Imaging;

namespace ImageTool.Shared;

/// <summary>
/// Ghép nhiều ảnh thành 1 (dùng ngoài UI, test được phần điều phối): Merge-to-HDR bằng Exposure Fusion
/// (tăng dynamic range thật từ chùm bracket) và Focus Stacking (nét toàn bộ từ chùm lấy nét khác nhau).
///
/// Decode mỗi file -> LinearImage, kiểm tra cùng kích thước, chạy thuật toán trong ImageTool.Imaging,
/// rồi ghi PNG (16-bit để giữ dải động). Trả đường dẫn file kết quả.
/// </summary>
public static class MergeService
{
    public enum Mode { Hdr, FocusStack, Panorama }

    /// <summary>
    /// Ghép danh sách file. Trả đường dẫn output, hoặc ném nếu &lt;2 ảnh / kích thước khác nhau / decode lỗi.
    /// outputPath null -> tự đặt tên cạnh file đầu (vd IMG_001_hdr.png).
    /// </summary>
    public static string Merge(IReadOnlyList<string> inputPaths, Mode mode, ImageDecoderRegistry decoders, string? outputPath = null)
    {
        if (inputPaths == null || inputPaths.Count < 2)
            throw new ArgumentException("Cần ít nhất 2 ảnh để ghép.", nameof(inputPaths));

        var images = new List<LinearImage>(inputPaths.Count);
        foreach (var p in inputPaths)
        {
            if (!decoders.CanDecode(p)) throw new NotSupportedException($"Không decode được: {p}");
            images.Add(decoders.Decode(p).Image);
        }

        // Panorama: ghép tuần tự 2 ảnh một (KHÔNG yêu cầu cùng kích thước).
        if (mode == Mode.Panorama)
        {
            var pano = images[0];
            for (int i = 1; i < images.Count; i++)
            {
                var r = PanoramaStitcher.Stitch(pano, images[i]);
                if (!r.Success) throw new InvalidOperationException(r.Error ?? "Ghép panorama thất bại.");
                pano = r.Image!;
            }
            string panoOut = outputPath ?? DefaultOutputPath(inputPaths[0], mode);
            ImageEncoder.Save(pano, panoOut, ImageEncoder.BitDepth.Sixteen);
            return panoOut;
        }

        int w = images[0].Width, h = images[0].Height;
        for (int i = 1; i < images.Count; i++)
            if (images[i].Width != w || images[i].Height != h)
                throw new InvalidOperationException("Các ảnh phải cùng kích thước (canh chỉnh/crop trước khi ghép).");

        LinearImage result = mode switch
        {
            Mode.FocusStack => FocusStack.Stack(images),
            _ => ExposureFusion.Fuse(images),
        };

        string outPath = outputPath ?? DefaultOutputPath(inputPaths[0], mode);
        ImageEncoder.Save(result, outPath, ImageEncoder.BitDepth.Sixteen);
        return outPath;
    }

    private static string DefaultOutputPath(string firstInput, Mode mode)
    {
        string dir = Path.GetDirectoryName(firstInput) ?? ".";
        string name = Path.GetFileNameWithoutExtension(firstInput);
        string suffix = mode switch
        {
            Mode.FocusStack => "_focusstack",
            Mode.Panorama => "_panorama",
            _ => "_hdr",
        };
        string outPath = Path.Combine(dir, name + suffix + ".png");
        // tránh ghi đè.
        int n = 1;
        while (File.Exists(outPath))
            outPath = Path.Combine(dir, $"{name}{suffix}_{n++}.png");
        return outPath;
    }
}
