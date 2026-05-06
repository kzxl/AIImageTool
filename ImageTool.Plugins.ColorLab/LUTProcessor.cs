using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Plugins.ColorLab;

public static class LUTProcessor
{
    public static void ApplyLUT(Image<Rgba32> image, LUT3D lut, float intensity = 1f)
    {
        if (lut == null || lut.Table == null || intensity <= 0) return;

        int size = lut.Size;
        float maxIndex = size - 1;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 p = ref row[x];
                    
                    float pr = p.R / 255f;
                    float pg = p.G / 255f;
                    float pb = p.B / 255f;

                    float xLut = pr * maxIndex;
                    float yLut = pg * maxIndex;
                    float zLut = pb * maxIndex;

                    int x0 = (int)Math.Floor(xLut);
                    int x1 = Math.Min(size - 1, x0 + 1);
                    float dx = xLut - x0;

                    int y0 = (int)Math.Floor(yLut);
                    int y1 = Math.Min(size - 1, y0 + 1);
                    float dy = yLut - y0;

                    int z0 = (int)Math.Floor(zLut);
                    int z1 = Math.Min(size - 1, z0 + 1);
                    float dz = zLut - z0;

                    var c000 = lut.Table[z0, y0, x0];
                    var c100 = lut.Table[z0, y0, x1];
                    var c010 = lut.Table[z0, y1, x0];
                    var c110 = lut.Table[z0, y1, x1];
                    var c001 = lut.Table[z1, y0, x0];
                    var c101 = lut.Table[z1, y0, x1];
                    var c011 = lut.Table[z1, y1, x0];
                    var c111 = lut.Table[z1, y1, x1];

                    var c00 = Lerp(c000, c100, dx);
                    var c10 = Lerp(c010, c110, dx);
                    var c01 = Lerp(c001, c101, dx);
                    var c11 = Lerp(c011, c111, dx);

                    var c0 = Lerp(c00, c10, dy);
                    var c1 = Lerp(c01, c11, dy);

                    var c = Lerp(c0, c1, dz);

                    float fr = pr + (c.R - pr) * intensity;
                    float fg = pg + (c.G - pg) * intensity;
                    float fb = pb + (c.B - pb) * intensity;

                    fr = Math.Clamp(fr, 0f, 1f);
                    fg = Math.Clamp(fg, 0f, 1f);
                    fb = Math.Clamp(fb, 0f, 1f);

                    p.R = (byte)(fr * 255);
                    p.G = (byte)(fg * 255);
                    p.B = (byte)(fb * 255);
                }
            }
        });
    }

    private static FloatColor Lerp(FloatColor a, FloatColor b, float t)
    {
        return new FloatColor(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t
        );
    }
}
