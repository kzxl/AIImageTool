using System;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Host;

/// <summary>
/// Phân vùng "chủ thể" (salient subject) bằng U²-Net ONNX -> sinh ảnh mask xám (L8 PNG) để
/// RasterMask trong pipeline nội suy & áp như 1 local mask (6.6). Chạy DirectML, fallback CPU.
///
/// LƯU Ý: cần model ONNX (auto-download qua IModelDownloader). Inference thật phải verify trên máy
/// có model + GPU; ở đây chỉ đảm bảo code/pipeline đúng.
/// </summary>
public sealed class OnnxSegmenter : IDisposable
{
    private readonly InferenceSession _session;
    private readonly int _size;
    private readonly string _inputName;

    public OnnxSegmenter(string modelPath, int size = 320)
    {
        var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        try { opts.AppendExecutionProvider_DML(0); } catch { /* fallback CPU */ }
        _session = new InferenceSession(modelPath, opts);
        _inputName = System.Linq.Enumerable.First(_session.InputMetadata.Keys);
        var dims = _session.InputMetadata[_inputName].Dimensions;
        _size = dims.Length >= 3 && dims[2] > 0 ? dims[2] : size;
    }

    /// <summary>
    /// Chạy segmentation trên ảnh, ghi mask xám ra <paramref name="maskOutPath"/> (PNG L8) ở độ
    /// phân giải model. RasterMask sẽ tự nội suy về kích thước ảnh khi áp.
    /// </summary>
    public void GenerateMask(string imagePath, string maskOutPath)
    {
        using var img = Image.Load<Rgba32>(imagePath);
        using var resized = img.Clone(c => c.Resize(_size, _size, KnownResamplers.Bicubic));

        // Chuẩn hoá NCHW, mean/std kiểu U²-Net (chia 255 rồi (x-mean)/std).
        var data = new float[1 * 3 * _size * _size];
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };
        int hw = _size * _size;
        resized.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < _size; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < _size; x++)
                {
                    var p = row[x];
                    int idx = y * _size + x;
                    data[0 * hw + idx] = (p.R / 255f - mean[0]) / std[0];
                    data[1 * hw + idx] = (p.G / 255f - mean[1]) / std[1];
                    data[2 * hw + idx] = (p.B / 255f - mean[2]) / std[2];
                }
            }
        });

        var tensor = new DenseTensor<float>(data, new[] { 1, 3, _size, _size });
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
        var outArr = System.Linq.Enumerable.First(results).AsTensor<float>().ToArray();

        // Output là bản đồ xác suất [hw] (hoặc [1,1,H,W]); chuẩn hoá min-max về 0..255.
        int n = Math.Min(outArr.Length, hw);
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < n; i++) { if (outArr[i] < min) min = outArr[i]; if (outArr[i] > max) max = outArr[i]; }
        float range = max - min;
        if (range < 1e-6f) range = 1f;

        using var maskImg = new Image<L8>(_size, _size);
        maskImg.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < _size; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < _size; x++)
                {
                    int i = y * _size + x;
                    float v = i < n ? (outArr[i] - min) / range : 0f;
                    row[x] = new L8((byte)Math.Clamp(v * 255f, 0, 255));
                }
            }
        });

        var dir = Path.GetDirectoryName(maskOutPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        maskImg.SaveAsPng(maskOutPath);
    }

    public void Dispose() => _session.Dispose();
}
