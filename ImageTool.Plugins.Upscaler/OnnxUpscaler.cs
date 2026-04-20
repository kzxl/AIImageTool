using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly byte[] _modelBytes;
    private readonly int _targetDeviceId;
    private readonly PerformanceMode _performanceMode;
    private static bool _nativePreloaded = false;

    public OnnxUpscaler(byte[] modelBytes, int targetDeviceId = -1, PerformanceMode performanceMode = PerformanceMode.Safe)
    {
        PreloadNativeLibraries();
        _modelBytes = modelBytes;
        _targetDeviceId = targetDeviceId;
        _performanceMode = performanceMode;
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

    public Image<Rgba32> Process(Image<Rgba32> image, IProgress<int>? progress = null, int targetScale = 4, System.Threading.CancellationToken ct = default)
    {
        try 
        {
            return ProcessReal(image, progress, targetScale, ct);
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

    private Image<Rgba32> ProcessReal(Image<Rgba32> image, IProgress<int>? progress, int targetScale, System.Threading.CancellationToken ct)
    {
        int width = image.Width;
        int height = image.Height;
        
        // TỐI ƯU SIÊU CẤP: Tính toán động cỡ mảnh theo năng lực phần cứng
        int tileSize = 128;
        if (_targetDeviceId >= 0)
        {
            // GPU Mode: Tống cục to để khai thác hết Stream Processors
             tileSize = (_performanceMode == PerformanceMode.Unleashed) ? 1024 : 512;
        }
        else 
        {
            // CPU Mode: Gồng nhẹ nhàng nương theo cache line 
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

        SessionOptions? options = null;
        InferenceSession? session = null;
        List<string> gpuInitErrors = new List<string>();

        if (_targetDeviceId >= 0)
        {
            try 
            {
                var tempOptions = new SessionOptions();
                tempOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                tempOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                tempOptions.AppendExecutionProvider_DML(_targetDeviceId); 
                session = new InferenceSession(_modelBytes, tempOptions);
                options = tempOptions;
            }
            catch (Exception ex)
            {
                gpuInitErrors.Add($"GPU {_targetDeviceId}: {ex.Message}");
            }
        }

        if (session == null)
        {
            if (_targetDeviceId >= 0 && gpuInitErrors.Any())
            {
                System.Diagnostics.Debug.WriteLine("GPU Fallback: " + string.Join(" | ", gpuInitErrors));
            }
            options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            
            if (_performanceMode == PerformanceMode.Unleashed)
                options.IntraOpNumThreads = Math.Max(1, (int)(Environment.ProcessorCount * 0.8)); 
            else
                options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2); 
            
            session = new InferenceSession(_modelBytes, options); 
        }

        var tileCoords = new List<(int Index, int X, int Y)>();
        int idx = 0;
        foreach (var startY in ys)
        {
            foreach (var startX in xs)
            {
                tileCoords.Add((idx++, startX, startY));
            }
        }

        object imageLock = new object();
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

            Image<Rgba32> tile;
            lock(imageLock)
            {
                tile = image.Clone(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropW, cropH)));
            }
            
            using var upscaledTile = ProcessTile(session, tile, cropW, cropH);
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

        options?.Dispose();
        session.Dispose();

        ct.ThrowIfCancellationRequested();

        if (targetScale != modelScale)
        {
            progress?.Report(99); 
            resultImage.Mutate(ctx => ctx.Resize(width * targetScale, height * targetScale, KnownResamplers.Lanczos3));
        }

        return resultImage;
    }

    private static readonly object _gpuGlobalLock = new object();

    private Image<Rgba32> ProcessTile(InferenceSession session, Image<Rgba32> tile, int width, int height)
    {
        var inputTensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        
        tile.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    inputTensor[0, 0, y, x] = row[x].R / 255f;
                    inputTensor[0, 1, y, x] = row[x].G / 255f;
                    inputTensor[0, 2, y, x] = row[x].B / 255f;
                }
            }
        });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor) 
        };

        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        try 
        {
            // BẮT BUỘC: DirectML không hỗ trợ thread-safe trên hàm Run(). Gửi đồng thời lệnh tới DirectML Queue sẽ gây Hard Crash app.
            if (_targetDeviceId >= 0)
            {
                lock (_gpuGlobalLock)
                {
                    results = session.Run(inputs);
                }
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

            int outHeight = output.Dimensions[2];
            int outWidth = output.Dimensions[3];

            var resultTile = new Image<Rgba32>(outWidth, outHeight);
            resultTile.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < outHeight; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < outWidth; x++)
                    {
                        row[x] = new Rgba32(
                            (float)Math.Clamp(output[0, 0, y, x], 0, 1),
                            (float)Math.Clamp(output[0, 1, y, x], 0, 1),
                            (float)Math.Clamp(output[0, 2, y, x], 0, 1),
                            1f
                        );
                    }
                }
            });

            return resultTile;
        }
    }

    private Image<Rgba32> SimulateProcess(Image<Rgba32> image, IProgress<int>? progress, System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cloned = image.Clone(x => x.Resize(image.Width * 2, image.Height * 2, KnownResamplers.Bicubic));
        progress?.Report(100);
        return cloned;
    }
}
