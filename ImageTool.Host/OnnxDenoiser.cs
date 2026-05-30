using System;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ImageTool.Imaging;

namespace ImageTool.Host;

/// <summary>
/// AI denoise bằng SCUNet ONNX (4.3). Cắm vào pipeline qua <see cref="AiOpHost.DenoiseProcessor"/>:
/// nhận LinearImage (sửa tại chỗ), strength để blend kết quả với gốc. Chạy DirectML, fallback CPU.
///
/// Model nhận sRGB 0..1 NCHW. Ta encode linear->sRGB trước khi vào model, decode ngược sau.
/// Strength &lt;1 blend tuyến tính giữ gốc/đã khử.
///
/// LƯU Ý: cần model ONNX; inference thật verify trên máy có model + GPU.
/// </summary>
public sealed class OnnxDenoiser : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;

    public OnnxDenoiser(string modelPath)
    {
        var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        try { opts.AppendExecutionProvider_DML(0); } catch { /* CPU */ }
        _session = new InferenceSession(modelPath, opts);
        _inputName = System.Linq.Enumerable.First(_session.InputMetadata.Keys);
    }

    /// <summary>Áp denoise lên ảnh linear (sửa tại chỗ), blend theo strength [0..1].</summary>
    public void Apply(LinearImage image, float strength, float scale)
    {
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;
        int hw = w * h;

        // linear -> sRGB NCHW.
        var data = new float[1 * 3 * hw];
        for (int i = 0; i < hw; i++)
        {
            int o = i * 4;
            data[0 * hw + i] = ColorSpace.LinearToSrgb(px[o]);
            data[1 * hw + i] = ColorSpace.LinearToSrgb(px[o + 1]);
            data[2 * hw + i] = ColorSpace.LinearToSrgb(px[o + 2]);
        }

        var tensor = new DenseTensor<float>(data, new[] { 1, 3, h, w });
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
        var outArr = System.Linq.Enumerable.First(results).AsTensor<float>().ToArray();
        if (outArr.Length < 3 * hw) return; // shape lạ -> bỏ qua an toàn

        float k = Math.Clamp(strength, 0f, 1f);
        for (int i = 0; i < hw; i++)
        {
            int o = i * 4;
            float dr = ColorSpace.SrgbToLinear(Clamp01(outArr[0 * hw + i]));
            float dg = ColorSpace.SrgbToLinear(Clamp01(outArr[1 * hw + i]));
            float db = ColorSpace.SrgbToLinear(Clamp01(outArr[2 * hw + i]));
            px[o] = px[o] + (dr - px[o]) * k;
            px[o + 1] = px[o + 1] + (dg - px[o + 1]) * k;
            px[o + 2] = px[o + 2] + (db - px[o + 2]) * k;
        }
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    public void Dispose() => _session.Dispose();
}
