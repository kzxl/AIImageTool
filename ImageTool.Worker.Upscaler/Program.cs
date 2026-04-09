using System;
using System.IO;
using ImageTool.Plugins.Upscaler;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Worker.Upscaler;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("[DEBUG] Worker Process Started.");
            
            string input = "";
            string output = "";
            int scale = 4;
            int deviceId = -1;
            PerformanceMode mode = PerformanceMode.Safe;
            string modelPath = "";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--input" && i + 1 < args.Length) input = args[++i];
                if (args[i] == "--out" && i + 1 < args.Length) output = args[++i];
                if (args[i] == "--scale" && i + 1 < args.Length) scale = int.Parse(args[++i]);
                if (args[i] == "--device" && i + 1 < args.Length) deviceId = int.Parse(args[++i]);
                if (args[i] == "--mode" && i + 1 < args.Length)
                {
                    if (args[++i].Equals("Unleashed", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = PerformanceMode.Unleashed;
                    }
                }
                if (args[i] == "--model" && i + 1 < args.Length) modelPath = args[++i];
            }

            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output) || string.IsNullOrEmpty(modelPath))
            {
                Console.WriteLine("[ERROR] Missing required arguments (--input, --out, --model).");
                Environment.Exit(1);
            }

            Console.WriteLine($"[DEBUG] Loading model from {modelPath}...");
            byte[] modelBytes = File.ReadAllBytes(modelPath);

            Console.WriteLine($"[DEBUG] Loading image from {input}...");
            using var image = Image.Load<Rgba32>(input);

            Console.WriteLine($"[DEBUG] Initializing Upscaler (Device: {deviceId}, Mode: {mode})...");
            var upscaler = new OnnxUpscaler(modelBytes, deviceId, mode);

            var progress = new Progress<int>(percent =>
            {
                Console.WriteLine($"[PROGRESS] {percent}");
            });

            Console.WriteLine("[DEBUG] Starting processing...");
            var result = upscaler.Process(image, progress, scale);

            Console.WriteLine($"[DEBUG] Process finished. Saving to {output}...");
            result.SaveAsPng(output);
            
            Console.WriteLine("[PROGRESS] 100");
            Console.WriteLine("[DEBUG] Worker Process Exited Successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(2);
        }
    }
}
