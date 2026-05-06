using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Plugins.ColorLab;

public static class ColorCorrectionProcessor
{
    public static void ApplyGrayWorld(Image<Rgba32> image)
    {
        long rSum = 0, gSum = 0, bSum = 0;
        int pixels = image.Width * image.Height;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    rSum += row[x].R;
                    gSum += row[x].G;
                    bSum += row[x].B;
                }
            }
        });

        float rAvg = (float)rSum / pixels;
        float gAvg = (float)gSum / pixels;
        float bAvg = (float)bSum / pixels;

        float grayAvg = (rAvg + gAvg + bAvg) / 3f;

        float rScale = grayAvg / Math.Max(rAvg, 1f);
        float gScale = grayAvg / Math.Max(gAvg, 1f);
        float bScale = grayAvg / Math.Max(bAvg, 1f);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 p = ref row[x];
                    p.R = (byte)Math.Clamp(p.R * rScale, 0, 255);
                    p.G = (byte)Math.Clamp(p.G * gScale, 0, 255);
                    p.B = (byte)Math.Clamp(p.B * bScale, 0, 255);
                }
            }
        });
    }

    public static void ApplyWhitePoint(Image<Rgba32> image, System.Windows.Media.Color grayPoint)
    {
        float rAvg = grayPoint.R;
        float gAvg = grayPoint.G;
        float bAvg = grayPoint.B;

        float grayAvg = (rAvg + gAvg + bAvg) / 3f;

        float rScale = grayAvg / Math.Max(rAvg, 1f);
        float gScale = grayAvg / Math.Max(gAvg, 1f);
        float bScale = grayAvg / Math.Max(bAvg, 1f);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 p = ref row[x];
                    p.R = (byte)Math.Clamp(p.R * rScale, 0, 255);
                    p.G = (byte)Math.Clamp(p.G * gScale, 0, 255);
                    p.B = (byte)Math.Clamp(p.B * bScale, 0, 255);
                }
            }
        });
    }
}
