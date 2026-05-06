using System;
using System.IO;

namespace ImageTool.Plugins.ColorLab;

public class LUT3D
{
    public string Title { get; set; }
    public int Size { get; set; }
    public FloatColor[,,] Table { get; set; }
}

public struct FloatColor
{
    public float R, G, B;
    public FloatColor(float r, float g, float b) { R = r; G = g; B = b; }
}

public static class LUTParser
{
    public static LUT3D ParseCubeFile(string path)
    {
        var lut = new LUT3D();
        var lines = File.ReadAllLines(path);
        
        int r = 0, g = 0, b = 0;

        foreach (var line in lines)
        {
            var l = line.Trim();
            if (string.IsNullOrEmpty(l) || l.StartsWith("#")) continue;

            if (l.StartsWith("TITLE"))
            {
                lut.Title = l.Replace("TITLE", "").Trim().Trim('"');
            }
            else if (l.StartsWith("LUT_3D_SIZE"))
            {
                lut.Size = int.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                lut.Table = new FloatColor[lut.Size, lut.Size, lut.Size];
            }
            else if (char.IsDigit(l[0]) || l[0] == '-' || l[0] == '.')
            {
                var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && lut.Table != null)
                {
                    float vr = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                    float vg = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    float vb = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    
                    lut.Table[b, g, r] = new FloatColor(vr, vg, vb);

                    r++;
                    if (r == lut.Size) { r = 0; g++; }
                    if (g == lut.Size) { g = 0; b++; }
                }
            }
        }
        
        if (string.IsNullOrEmpty(lut.Title))
            lut.Title = Path.GetFileNameWithoutExtension(path);

        return lut;
    }
}
