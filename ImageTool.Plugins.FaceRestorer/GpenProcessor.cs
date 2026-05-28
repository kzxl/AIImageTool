using System;
using System.Buffers;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Plugins.FaceRestorer;

/// <summary>
/// GPEN-BFR-512 wrapper. Input 512x512 RGB float [-1,1], output cùng shape.
/// </summary>
public class GpenProcessor : IDisposable
{
    private readonly InferenceSession _session;
    private const int Size = 512;

    public GpenProcessor(string modelPath)
    {
        var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        try { opts.AppendExecutionProvider_DML(0); } catch { }
        _session = new InferenceSession(modelPath, opts);
    }

    public Image<Rgba32> Process(Image<Rgba32> source, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(5);
        // Resize toàn ảnh về 512 (Phase 1: chỉ chạy ảnh chân dung)
        using var work = source.Clone(c => c.Resize(Size, Size, KnownResamplers.Bicubic));
        progress?.Report(20);
        ct.ThrowIfCancellationRequested();

        int plane = Size * Size;
        var pool = ArrayPool<float>.Shared;
        var buf = pool.Rent(3 * plane);
        try
        {
            int rOff = 0, gOff = plane, bOff = 2 * plane;
            const float inv = 1f / 127.5f;
            work.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < Size; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int off = y * Size;
                    for (int x = 0; x < Size; x++)
                    {
                        var px = row[x];
                        buf[rOff + off + x] = px.R * inv - 1f;
                        buf[gOff + off + x] = px.G * inv - 1f;
                        buf[bOff + off + x] = px.B * inv - 1f;
                    }
                }
            });

            var inputName = _session.InputMetadata.Keys.First();
            var tensor = new DenseTensor<float>(new Memory<float>(buf, 0, 3 * plane),
                new[] { 1, 3, Size, Size });
            progress?.Report(30);

            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
            progress?.Report(70);
            ct.ThrowIfCancellationRequested();

            var output = results.First().AsTensor<float>();
            float[] flat;
            if (output is DenseTensor<float> dense) flat = dense.Buffer.ToArray();
            else flat = output.ToArray();

            int rO = 0, gO = plane, bO = 2 * plane;
            var result = new Image<Rgba32>(Size, Size);
            result.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < Size; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int off = y * Size;
                    for (int x = 0; x < Size; x++)
                    {
                        row[x] = new Rgba32(
                            ToByte(flat[rO + off + x]),
                            ToByte(flat[gO + off + x]),
                            ToByte(flat[bO + off + x]),
                            (byte)255);
                    }
                }
            });

            progress?.Report(95);

            // Resize lại kích thước gốc
            if (source.Width != Size || source.Height != Size)
            {
                result.Mutate(c => c.Resize(source.Width, source.Height, KnownResamplers.Bicubic));
            }

            progress?.Report(100);
            return result;
        }
        finally
        {
            pool.Return(buf);
        }
    }

    private static byte ToByte(float v)
    {
        // Output [-1, 1] -> [0, 255]
        float n = (v + 1f) * 127.5f;
        if (n <= 0) return 0;
        if (n >= 255f) return 255;
        return (byte)(n + 0.5f);
    }

    public void Dispose() => _session.Dispose();
}
