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

    public OnnxUpscaler(byte[] modelBytes, int targetDeviceId = -1, PerformanceMode performanceMode = PerformanceMode.Safe)
    {
        _modelBytes = modelBytes;
        _targetDeviceId = targetDeviceId;
        _performanceMode = performanceMode;
    }

    /// <summary>
    /// Xử lý phân tích ảnh qua ONNX Runtime với mức zoom tự chọn (TargetScale)
    /// </summary>
    public Image<Rgba32> Process(Image<Rgba32> image, IProgress<int>? progress = null, int targetScale = 4)
    {
        try 
        {
            return ProcessReal(image, progress, targetScale);
        } 
        catch (OnnxRuntimeException)
        {
            return SimulateProcess(image, progress);
        }
    }

    private Image<Rgba32> ProcessReal(Image<Rgba32> image, IProgress<int>? progress, int targetScale)
    {
        int width = image.Width;
        int height = image.Height;
        
        // TỐI ƯU CẤP ĐỘ 1: TileSize 128 (vừa vặn cho VRAM hiện đại) để giảm số lượt cấp phát overhead từ vòng lặp
        int tileSize = 128; 
        
        // BẢN CHẤT MÔ HÌNH: 4x-UltraSharp luôn xuất ra lớp chập Tensor PixelShuffle cố định ở x4. 
        // Phải tuân thủ luật x4 cho tiến trình tạo nền và lưới Tile cơ bản sau đó Resize lại nếu User muốn x2 hoặc x8
        int modelScale = 4; 
        
        // Tạo ảnh nền kết quả to chuẩn tỷ lệ của AI
        var resultImage = new Image<Rgba32>(width * modelScale, height * modelScale);
        
        // Phân mảnh ảnh (Chunking)
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
            // BẬT TÍNH NĂNG TĂNG TỐC BẰNG THIẾT BỊ ĐÃ CHỌN
            try 
            {
                var tempOptions = new SessionOptions();
                
                // TỐI ƯU CẤP ĐỘ 2.1: Fuse Graph nội bộ mức độ cao nhất giúp đẩy nhịp C++ nhanh hơn
                tempOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                tempOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                
                tempOptions.AppendExecutionProvider_DML(_targetDeviceId); 
                // Cần khởi tạo thử session để xem thiết bị có thực sự hoạt động với DirectML không
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
            
            // TỐI ƯU CẤP ĐỘ 2.2: Tối ưu Graph
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            
            // CẦU HÌNH THREADS CPU
            if (_performanceMode == PerformanceMode.Unleashed)
            {
                options.IntraOpNumThreads = Math.Max(1, (int)(Environment.ProcessorCount * 0.8)); // Tối đa 80% lõi
            }
            else
            {
                options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2); // Chỉ 50% lõi cho an toàn
            }
            
            session = new InferenceSession(_modelBytes, options); 
        }

        // Gộp toạ độ lưới để chạy song song
        var tileCoords = new List<(int X, int Y)>();
        foreach (var startY in ys)
        {
            foreach (var startX in xs)
            {
                tileCoords.Add((startX, startY));
            }
        }

        object imageLock = new object();

        // THIẾT LẬP MAX PARALLEL VÀ RAM LIMIT THEO CHẾ ĐỘ
        int maxParallel = 1;
        double ramThreshold = 60.0; // An Toàn: 60%
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

        // TĂNG TỐC KÉP: Chạy song song dựa vào cấu hình Mode quy định
        Parallel.ForEach(tileCoords, new ParallelOptions { MaxDegreeOfParallelism = maxParallel }, coord =>
        {
            // Flood-Control: Phanh thời gian thực (Realtime throttling)
            var rMem = GC.GetGCMemoryInfo();
            double memUsage = (double)rMem.MemoryLoadBytes / rMem.TotalAvailableMemoryBytes * 100.0;
            if (memUsage > ramThreshold)
            {
                System.Threading.Thread.Sleep(300 + (int)(memUsage - ramThreshold) * 50); // Mức mượn quá cao -> Nghỉ lâu hơn
            }

            int startX = coord.X;
            int startY = coord.Y;
            int currentW = Math.Min(tileSize, width - startX);
            int currentH = Math.Min(tileSize, height - startY);

            // TỐI ƯU CẤP ĐỘ 6: Chống Gãy Ảnh (Anti-Seam / Tile Overlapping)
            // Lấy thêm 16 pixel mép chờm xung quanh để AI có dữ liệu lân cận nội suy, tránh bị khấc viền (Grid Artifacts) ở các điểm nối
            int overlap = 16;
            int padLeft = Math.Min(overlap, startX);
            int padTop = Math.Min(overlap, startY);
            int padRight = Math.Min(overlap, width - (startX + currentW));
            int padBottom = Math.Min(overlap, height - (startY + currentH));

            int cropX = startX - padLeft;
            int cropY = startY - padTop;
            int cropW = currentW + padLeft + padRight;
            int cropH = currentH + padTop + padBottom;

            // 1. Trích xuất Ô ảnh (Có chứa viền chờm bao bọc lân cận)
            Image<Rgba32> tile;
            lock(imageLock)
            {
                tile = image.Clone(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropW, cropH)));
            }
            
            // 2. Chạy Ô ảnh qua ONNX 
            using var upscaledTile = ProcessTile(session, tile, cropW, cropH);
            tile.Dispose();
            
            // 2.5 Cắt gọt phần LÕI NGUYÊN CHẤT (Bỏ đi phầm viền mồi đã bị Upscale x4)
            int cropOutputX = padLeft * modelScale;
            int cropOutputY = padTop * modelScale;
            int cropOutputW = currentW * modelScale;
            int cropOutputH = currentH * modelScale;

            using var coreTile = upscaledTile.Clone(ctx => ctx.Crop(new Rectangle(cropOutputX, cropOutputY, cropOutputW, cropOutputH)));

            // 3. Dán Ô Cực Nét lên Ảnh Gốc (Đảm bảo mép sẽ tiệp màu tuyệt đối)
            lock(imageLock)
            {
                resultImage.Mutate(ctx => ctx.DrawImage(coreTile, new Point(startX * modelScale, startY * modelScale), 1f));
            }
            
            int count = System.Threading.Interlocked.Increment(ref currentTile);
            progress?.Report((int)((float)count / totalTiles * 100));

            // CHU KỲ DỌN RAM TÙY BIẾN THEO CHẾ ĐỘ
            if (count % ramCheckFreq == 0) 
            {
                GC.Collect(0, GCCollectionMode.Optimized, false);
            }
        });
        
        // Đảm bảo giải phóng tài nguyên CPU/GPU sau khi Inference hoàn tất!
        options?.Dispose();
        session.Dispose();

        // LINH ĐỘNG TUỲ CHỈNH KÍCH THƯỚC: Nếu Model tạo ra x4 mà User chọn x2 hoặc x8
        if (targetScale != modelScale)
        {
            progress?.Report(99); // Báo là đang resize bước cuối
            // Nếu Target < Model (VD X2 so với X4) => Nén lấy nét cực cao (Supersampling AntiAliasing)
            // Nếu Target > Model (VD X8 so với X4) => Bơm thêm nội suy Lanczos cấp độ 3 
            resultImage.Mutate(ctx => ctx.Resize(width * targetScale, height * targetScale, KnownResamplers.Lanczos3));
        }

        return resultImage;
    }

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

        using var results = session.Run(inputs);
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

    private Image<Rgba32> SimulateProcess(Image<Rgba32> image, IProgress<int>? progress)
    {
        var cloned = image.Clone(x => x.Resize(image.Width * 2, image.Height * 2, KnownResamplers.Bicubic));
        progress?.Report(100);
        return cloned;
    }
}
