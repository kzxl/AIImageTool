using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Plugins.Upscaler;

public enum PerformanceMode
{
    Safe,       // 1. an toàn giữ cpu không và ram không quá cao
    Unleashed   // 2. chạy bung lụa, cpu và ram không quá 80% 
}

public class OnnxUpscaler
{
    private readonly string _modelPath;
    private readonly int _targetDeviceId;
    private readonly PerformanceMode _performanceMode;
    private static bool _nativePreloaded = false;

    // Session pool: key = (modelPath, deviceId, perfMode). Reuse giữa các lần Process().
    private record struct SessionKey(string ModelPath, int DeviceId, PerformanceMode Mode);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<SessionKey, Lazy<InferenceSession>> _sessionPool = new();

    public OnnxUpscaler(string modelPath, int targetDeviceId = -1, PerformanceMode performanceMode = PerformanceMode.Safe)
    {
        PreloadNativeLibraries();
        _modelPath = modelPath;
        _targetDeviceId = targetDeviceId;
        _performanceMode = performanceMode;
    }

    public static void DisposePool()
    {
        foreach (var kv in _sessionPool)
        {
            try { if (kv.Value.IsValueCreated) kv.Value.Value.Dispose(); } catch { }
        }
        _sessionPool.Clear();
    }

    private InferenceSession GetOrCreateSession(out bool usedGpu, List<string> gpuInitErrors)
    {
        usedGpu = false;
        if (_targetDeviceId >= 0)
        {
            var key = new SessionKey(_modelPath, _targetDeviceId, _performanceMode);
            try
            {
                var lazy = _sessionPool.GetOrAdd(key, k => new Lazy<InferenceSession>(() =>
                {
                    var opts = new SessionOptions
                    {
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
                    };
                    opts.AppendExecutionProvider_DML(k.DeviceId);
                    return new InferenceSession(k.ModelPath, opts);
                }, true));
                var s = lazy.Value;
                usedGpu = true;
                return s;
            }
            catch (Exception ex)
            {
                gpuInitErrors.Add($"GPU {_targetDeviceId}: {ex.Message}");
                _sessionPool.TryRemove(key, out _);
            }
        }

        // CPU fallback
        var cpuKey = new SessionKey(_modelPath, -1, _performanceMode);
        var cpuLazy = _sessionPool.GetOrAdd(cpuKey, k => new Lazy<InferenceSession>(() =>
        {
            var opts = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
            };
            opts.IntraOpNumThreads = k.Mode == PerformanceMode.Unleashed
                ? Math.Max(1, (int)(Environment.ProcessorCount * 0.8))
                : Math.Max(1, Environment.ProcessorCount / 2);
            return new InferenceSession(k.ModelPath, opts);
        }, true));
        return cpuLazy.Value;
    }

    private static void PreloadNativeLibraries()
    {
        if (_nativePreloaded) return;
        try 
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string directMlPath = System.IO.Path.Combine(baseDir, "DirectML.dll");
            if (System.IO.File.Exists(directMlPath))
                System.Runtime.InteropServices.NativeLibrary.TryLoad(directMlPath, out _);

            string onnxSharedPath = System.IO.Path.Combine(baseDir, "onnxruntime_providers_shared.dll");
            if (System.IO.File.Exists(onnxSharedPath))
                System.Runtime.InteropServices.NativeLibrary.TryLoad(onnxSharedPath, out _);

            string onnxPath = System.IO.Path.Combine(baseDir, "onnxruntime.dll");
            if (System.IO.File.Exists(onnxPath))
                System.Runtime.InteropServices.NativeLibrary.TryLoad(onnxPath, out _);

            _nativePreloaded = true;
        }
        catch { }
    }

    public Image<Rgba32> Process(Image<Rgba32> image, IProgress<int>? progress = null, int targetMp = 24, System.Threading.CancellationToken ct = default)
    {
        try 
        {
            return ProcessReal(image, progress, targetMp, ct);
        } 
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation
        }
        catch (Exception ex)
        {
            // Bắt cẩn thận tất cả lỗi (đặc biệt AggregateException từ Parallel.ForEach nếu GPU crash giữa chừng)
            System.Diagnostics.Debug.WriteLine($"[Process Fallback] ONNX Error: {ex.GetType().Name} - {ex.Message}");
            if (ex is AggregateException agEx)
            {
                foreach (var inner in agEx.InnerExceptions)
                    System.Diagnostics.Debug.WriteLine($"  -> Inner: {inner.Message}");
            }
            return SimulateProcess(image, progress, ct);
        }
    }

    private Image<Rgba32> ProcessReal(Image<Rgba32> image, IProgress<int>? progress, int targetMp, System.Threading.CancellationToken ct)
    {
        int width = image.Width;
        int height = image.Height;

        var gpuInitErrors = new List<string>();
        var session = GetOrCreateSession(out bool usedGpu, gpuInitErrors);
        if (!usedGpu && _targetDeviceId >= 0 && gpuInitErrors.Any())
        {
            System.Diagnostics.Debug.WriteLine("GPU Fallback: " + string.Join(" | ", gpuInitErrors));
        }

        var inputMeta = session.InputMetadata.Values.First();
        bool isStaticShape = inputMeta.Dimensions.Length >= 4 && inputMeta.Dimensions[2] > 0 && inputMeta.Dimensions[3] > 0;
        int fixedH = isStaticShape ? inputMeta.Dimensions[2] : -1;
        int fixedW = isStaticShape ? inputMeta.Dimensions[3] : -1;

        int overlap = 16;
        int tileSize = 128;
        
        if (isStaticShape)
        {
            tileSize = Math.Min(tileSize, fixedW - (overlap * 2));
            if (tileSize <= 0) tileSize = fixedW; 
        }
        else if (_targetDeviceId >= 0)
        {
             tileSize = (_performanceMode == PerformanceMode.Unleashed) ? 1024 : 512;
        }
        else 
        {
             tileSize = (_performanceMode == PerformanceMode.Unleashed) ? 256 : 128;
        }
        
        int modelScale = 4; 
        
        var resultImage = new Image<Rgba32>(width * modelScale, height * modelScale);
        
        var xs = new List<int>();
        var ys = new List<int>();
        for (int y = 0; y < height; y += tileSize) ys.Add(y);
        for (int x = 0; x < width; x += tileSize) xs.Add(x);
        
        int totalTiles = xs.Count * ys.Count;
        int currentTile = 0;

        var tileCoords = new List<(int Index, int X, int Y)>();
        int idx = 0;
        foreach (var startY in ys)
        {
            foreach (var startX in xs)
            {
                tileCoords.Add((idx++, startX, startY));
            }
        }

        int maxParallel = 1;
        double ramThreshold = 60.0;
        int ramCheckFreq = 1;

        if (_performanceMode == PerformanceMode.Unleashed)
        {
             maxParallel = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));
             ramThreshold = 80.0;
             ramCheckFreq = 3;
        }
        else
        {
             maxParallel = Math.Max(1, Environment.ProcessorCount / 4);
             ramThreshold = 60.0;
             ramCheckFreq = 1;
        }

        // TỐI ƯU SIÊU CẤP 3: Sử dụng System.Threading.Channels để lập Pipeline Producer-Consumer vắt kiệt RAM
        var parallelOptions = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = ct 
        };

        // Dải ống (Channel) nối từ Threads Sản xuất (Crop/Inference) sang Thread Tiêu thụ (Merge)
        // BoundedChannel giới hạn số lượng Tensor chờ ghép tránh nổ RAM
        var mergeChannel = System.Threading.Channels.Channel.CreateBounded<(int Index, int X, int Y, Image<Rgba32> CoreTile)>(
            new System.Threading.Channels.BoundedChannelOptions(Math.Max(10, maxParallel * 2)) 
            { FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait }
        );

        // --- CONSUMER THREAD: Công nhân Nhặt và Ghép ảnh ---
        var mergerTask = Task.Run(async () =>
        {
            await foreach (var pt in mergeChannel.Reader.ReadAllAsync(ct))
            {
                resultImage.Mutate(ctx => ctx.DrawImage(pt.CoreTile, new Point(pt.X, pt.Y), 1f));
                pt.CoreTile.Dispose(); // Gỡ cấu trúc đồ thị Pixel khỏi RAM NGAY LẬP TỨC

                int count = System.Threading.Interlocked.Increment(ref currentTile);
                progress?.Report((int)((float)count / totalTiles * 99)); // Chạm mốc 99%
                
                if (count % ramCheckFreq == 0) 
                {
                    GC.Collect(0, GCCollectionMode.Optimized, false);
                }
            }
        }, ct);

        // --- PRODUCER THREADS: Băng chuyền Gọt khối & Ép Tensor ---
        Parallel.ForEach(tileCoords, parallelOptions, coord =>
        {
            ct.ThrowIfCancellationRequested();

            var rMem = GC.GetGCMemoryInfo();
            double memUsage = (double)rMem.MemoryLoadBytes / rMem.TotalAvailableMemoryBytes * 100.0;
            if (memUsage > ramThreshold)
            {
                System.Threading.Thread.Sleep(300 + (int)(memUsage - ramThreshold) * 50); 
            }

            int startX = coord.X;
            int startY = coord.Y;
            int currentW = Math.Min(tileSize, width - startX);
            int currentH = Math.Min(tileSize, height - startY);

            int overlap = 16;
            int padLeft = Math.Min(overlap, startX);
            int padTop = Math.Min(overlap, startY);
            int padRight = Math.Min(overlap, width - (startX + currentW));
            int padBottom = Math.Min(overlap, height - (startY + currentH));

            int cropX = startX - padLeft;
            int cropY = startY - padTop;
            int cropW = currentW + padLeft + padRight;
            int cropH = currentH + padTop + padBottom;

            Image<Rgba32> tile = image.Clone(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropW, cropH)));
            
            using var upscaledTile = ProcessTile(session, tile, cropW, cropH, fixedW, fixedH);
            tile.Dispose();
            
            int cropOutputX = padLeft * modelScale;
            int cropOutputY = padTop * modelScale;
            int cropOutputW = currentW * modelScale;
            int cropOutputH = currentH * modelScale;

            // Xén khối lõi
            var coreTile = upscaledTile.Clone(ctx => ctx.Crop(new Rectangle(cropOutputX, cropOutputY, cropOutputW, cropOutputH)));

            // Bắn vào băng chuyền Merger
            mergeChannel.Writer.WriteAsync((coord.Index, coord.X * modelScale, coord.Y * modelScale, coreTile), ct).AsTask().Wait();
        });

        // Đóng nắp băng chuyền
        mergeChannel.Writer.Complete();
        // Chờ công nhân ghép xong
        mergerTask.Wait(ct);

        // Pool giữ session cho lần sau, không dispose ở đây.

        ct.ThrowIfCancellationRequested();
        
        long currentPixels = (long)width * height;
        long targetPixels = targetMp * 1000000L;
        long upscaledPixels = (long)resultImage.Width * resultImage.Height;

        // Cho phép scale-down (thu nhỏ nếu model x4 ra ảnh vỡ RAM) VÀ scale-up thêm (nếu model chưa đạt target MP)
        if (Math.Abs(upscaledPixels - targetPixels) > 100000) 
        {
            progress?.Report(99); 
            double finalScaleFactor = Math.Sqrt((double)targetPixels / currentPixels);
            int finalW = (int)(width * finalScaleFactor);
            int finalH = (int)(height * finalScaleFactor);
            
            resultImage.Mutate(ctx => ctx.Resize(finalW, finalH, KnownResamplers.Lanczos3));
        }

        return resultImage;
    }

    private static readonly object _gpuGlobalLock = new object();

    private Image<Rgba32> ProcessTile(InferenceSession session, Image<Rgba32> tile, int width, int height, int fixedW, int fixedH)
    {
        int inputH = fixedH > 0 ? fixedH : height;
        int inputW = fixedW > 0 ? fixedW : width;
        int planeSize = inputH * inputW;

        // Mượn buffer 3 planes (R/G/B) từ pool, tái sử dụng giữa các tile
        var pool = ArrayPool<float>.Shared;
        float[] inputBuf = pool.Rent(3 * planeSize);
        try
        {
            int rOff = 0, gOff = planeSize, bOff = 2 * planeSize;
            Array.Clear(inputBuf, 0, 3 * planeSize); // padding zeros nếu tile nhỏ hơn fixed

            const float inv255 = 1f / 255f;
            int innerW = width;
            int innerH = height;
            int strideIn = inputW;
            tile.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < innerH; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int off = y * strideIn;
                    for (int x = 0; x < innerW; x++)
                    {
                        var px = row[x];
                        inputBuf[rOff + off + x] = px.R * inv255;
                        inputBuf[gOff + off + x] = px.G * inv255;
                        inputBuf[bOff + off + x] = px.B * inv255;
                    }
                }
            });

            var inputTensor = new DenseTensor<float>(new Memory<float>(inputBuf, 0, 3 * planeSize),
                new[] { 1, 3, inputH, inputW });

            var inputName = session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
            try
            {
                if (_targetDeviceId >= 0)
                {
                    lock (_gpuGlobalLock) { results = session.Run(inputs); }
                }
                else
                {
                    results = session.Run(inputs);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"ONNX Inference Exception: {ex.Message}", ex);
            }

            using (results)
            {
                var outputName = session.OutputMetadata.Keys.First();
                var output = results.First(v => v.Name == outputName).AsTensor<float>();

                int outModelHeight = output.Dimensions[2];
                int outModelWidth = output.Dimensions[3];
                int scale = outModelHeight / inputH;
                int outValidHeight = height * scale;
                int outValidWidth = width * scale;
                int outPlane = outModelHeight * outModelWidth;

                // Đọc trực tiếp từ Tensor buffer khi có thể, tránh ToArray() sao chép full output (vài MB/tile).
                // Memory<T> capture được vào lambda (Span<T> là ref struct nên không capture được).
                ReadOnlyMemory<float> oFlat = output is DenseTensor<float> dense
                    ? dense.Buffer
                    : new ReadOnlyMemory<float>(output.ToArray());

                int rOut = 0, gOut = outPlane, bOut = 2 * outPlane;
                int strideOut = outModelWidth;

                var resultTile = new Image<Rgba32>(outValidWidth, outValidHeight);
                resultTile.ProcessPixelRows(accessor =>
                {
                    var span = oFlat.Span;
                    for (int y = 0; y < outValidHeight; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        int off = y * strideOut;
                        int rBase = rOut + off;
                        int gBase = gOut + off;
                        int bBase = bOut + off;
                        for (int x = 0; x < outValidWidth; x++)
                        {
                            byte rb = FloatToByte(span[rBase + x]);
                            byte gb = FloatToByte(span[gBase + x]);
                            byte bb = FloatToByte(span[bBase + x]);
                            row[x] = new Rgba32(rb, gb, bb, (byte)255);
                        }
                    }
                });

                return resultTile;
            }
        }
        finally
        {
            pool.Return(inputBuf);
        }
    }

    private static byte FloatToByte(float v)
    {
        if (v <= 0f) return 0;
        if (v >= 1f) return 255;
        return (byte)(v * 255f + 0.5f);
    }

    private Image<Rgba32> SimulateProcess(Image<Rgba32> image, IProgress<int>? progress, System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cloned = image.Clone(x => x.Resize(image.Width * 2, image.Height * 2, KnownResamplers.Bicubic));
        progress?.Report(100);
        return cloned;
    }
}
