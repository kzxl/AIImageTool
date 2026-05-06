using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;

namespace ImageTool.Plugins.ColorLab;

public static class AdvancedColorProcessor
{
    public class DominantColorInfo
    {
        public string Hex { get; set; }
        public float Percentage { get; set; }
        public System.Windows.Media.Color WpfColor => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Hex);
    }

    /// <summary>
    /// K-Means Clustering for Dominant Colors extraction.
    /// Resizes the image aggressively to speed up processing.
    /// </summary>
    public static List<DominantColorInfo> GetKMeansColors(Image<Rgba32> sourceImage, int k = 5, int maxIterations = 10)
    {
        // Resize for performance
        using var smallImg = sourceImage.Clone(x => x.Resize(Math.Min(128, sourceImage.Width), Math.Min(128, sourceImage.Height)));
        
        var pixels = new List<Rgb>();
        smallImg.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                foreach (ref Rgba32 p in row)
                {
                    // Ignore fully transparent or pure black/white
                    if (p.A < 255 || (p.R < 20 && p.G < 20 && p.B < 20) || (p.R > 240 && p.G > 240 && p.B > 240)) 
                        continue;
                    
                    pixels.Add(new Rgb(p.R / 255f, p.G / 255f, p.B / 255f));
                }
            }
        });

        if (pixels.Count == 0) return new List<DominantColorInfo>();

        var random = new Random(42);
        var centers = pixels.OrderBy(x => random.Next()).Take(k).ToList();
        var clusters = new List<Rgb>[k];
        
        for (int i = 0; i < maxIterations; i++)
        {
            for (int c = 0; c < k; c++) clusters[c] = new List<Rgb>();

            foreach (var p in pixels)
            {
                int bestCluster = 0;
                float minDistance = float.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    float dR = p.R - centers[c].R;
                    float dG = p.G - centers[c].G;
                    float dB = p.B - centers[c].B;
                    float dist = dR * dR + dG * dG + dB * dB;
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestCluster = c;
                    }
                }
                clusters[bestCluster].Add(p);
            }

            bool changed = false;
            for (int c = 0; c < k; c++)
            {
                if (clusters[c].Count > 0)
                {
                    float avgR = clusters[c].Average(x => x.R);
                    float avgG = clusters[c].Average(x => x.G);
                    float avgB = clusters[c].Average(x => x.B);
                    var newCenter = new Rgb(avgR, avgG, avgB);
                    if (Math.Abs(newCenter.R - centers[c].R) > 0.01f || Math.Abs(newCenter.G - centers[c].G) > 0.01f)
                    {
                        changed = true;
                        centers[c] = newCenter;
                    }
                }
            }
            if (!changed) break;
        }

        var results = new List<DominantColorInfo>();
        int totalValidPixels = pixels.Count;
        for (int i = 0; i < k; i++)
        {
            if (clusters[i].Count > 0)
            {
                float pct = (float)clusters[i].Count / totalValidPixels;
                byte r = (byte)Math.Clamp(centers[i].R * 255, 0, 255);
                byte g = (byte)Math.Clamp(centers[i].G * 255, 0, 255);
                byte b = (byte)Math.Clamp(centers[i].B * 255, 0, 255);
                results.Add(new DominantColorInfo {
                    Hex = $"#{r:X2}{g:X2}{b:X2}",
                    Percentage = pct
                });
            }
        }

        return results.OrderByDescending(x => x.Percentage).ToList();
    }

    /// <summary>
    /// Unifies image tone towards a target color. Modifies chroma slightly towards target.
    /// </summary>
    public static void ApplyColorUnification(Image<Rgba32> image, System.Windows.Media.Color baseColor, float intensity)
    {
        var targetHsl = ColorSpaceConverter.ToHsl(new Rgb(baseColor.R / 255f, baseColor.G / 255f, baseColor.B / 255f));
        float tH = targetHsl.H;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 p = ref row[x];
                    var hsl = ColorSpaceConverter.ToHsl(new Rgb(p.R / 255f, p.G / 255f, p.B / 255f));
                    
                    // Don't shift fully desaturated pixels completely
                    if (hsl.S < 0.05f) continue;

                    // Calculate shortest distance on Hue wheel
                    float diff = tH - hsl.H;
                    if (diff > 180) diff -= 360;
                    else if (diff < -180) diff += 360;

                    // Move hue towards target gently
                    float moveAmount = diff * intensity;
                    float newH = hsl.H + moveAmount;
                    if (newH < 0) newH += 360;
                    if (newH >= 360) newH -= 360;

                    // Also blend Saturation slightly if target is less saturated
                    float newS = hsl.S + ((targetHsl.S - hsl.S) * (intensity * 0.5f));

                    var newRgb = ColorSpaceConverter.ToRgb(new Hsl(newH, newS, hsl.L));
                    p.R = (byte)Math.Clamp(newRgb.R * 255, 0, 255);
                    p.G = (byte)Math.Clamp(newRgb.G * 255, 0, 255);
                    p.B = (byte)Math.Clamp(newRgb.B * 255, 0, 255);
                }
            }
        });
    }

    /// <summary>
    /// Applies edge-preserving noise reduction or chrominance blurring 
    /// Fast approximation: Simple quantization or box filter to denoise.
    /// </summary>
    public static void ApplyColorNoiseReduction(Image<Rgba32> image, int strength = 1)
    {
        // Simple smoothing using ImageSharp's BoxBlur applied slightly.
        // A full Bilateral filter could be heavy.
        // Let's use a subtle BoxBlur just enough to reduce noise. 
        image.Mutate(x => x.GaussianBlur(0.8f * strength));
        // Note: For true color noise reduction, converting to Lab/YCbCr and blurring chroma channels is best,
        // but GaussianBlur across RGB with small radius simulates a denoiser nicely while being fast.
    }
}
