using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Sinh 1 mask float (0..1) trên kích thước ảnh. Toạ độ tham số ở dạng CHUẨN HOÁ [0..1]
/// theo chiều rộng/cao để mask khớp giữa proxy và full-res (không phụ thuộc px).
/// </summary>
public interface IMaskGenerator
{
    string MaskType { get; }
    /// <summary>Trả mask dài W*H, mỗi phần tử 0..1 (1 = áp đầy đủ).</summary>
    float[] Generate(int width, int height);
    Dictionary<string, string> ToParams();
}

/// <summary>Mask gradient tuyến tính (graduated filter): chuyển 0->1 theo 1 đường thẳng.</summary>
public sealed class LinearGradientMask : IMaskGenerator
{
    public const string Type = "LinearGradient";
    public string MaskType => Type;

    // 2 điểm chuẩn hoá: bắt đầu (mask=0) -> kết thúc (mask=1).
    public float X0, Y0, X1 = 0f, Y1 = 1f;
    public bool Invert;

    public float[] Generate(int width, int height)
    {
        var m = new float[width * height];
        float dx = (X1 - X0), dy = (Y1 - Y0);
        float len2 = dx * dx + dy * dy;
        if (len2 < 1e-9f) { Array.Fill(m, 1f); return m; }

        Parallel.For(0, height, y =>
        {
            float ny = height <= 1 ? 0f : y / (float)(height - 1);
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float nx = width <= 1 ? 0f : x / (float)(width - 1);
                float t = ((nx - X0) * dx + (ny - Y0) * dy) / len2;
                t = Math.Clamp(t, 0f, 1f);
                float s = t * t * (3f - 2f * t); // smoothstep
                m[row + x] = Invert ? 1f - s : s;
            }
        });
        return m;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mask"] = Type, ["x0"] = F(X0), ["y0"] = F(Y0), ["x1"] = F(X1), ["y1"] = F(Y1), ["invert"] = Invert ? "true" : "false",
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static LinearGradientMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        X0 = EditOpRegistry.F(p, "x0"), Y0 = EditOpRegistry.F(p, "y0"),
        X1 = EditOpRegistry.F(p, "x1"), Y1 = EditOpRegistry.F(p, "y1", 1f),
        Invert = EditOpRegistry.B(p, "invert"),
    };
}

/// <summary>Mask radial (elip): trong vùng = 1, ngoài giảm dần theo feather.</summary>
public sealed class RadialMask : IMaskGenerator
{
    public const string Type = "Radial";
    public string MaskType => Type;

    public float Cx = 0.5f, Cy = 0.5f;  // tâm chuẩn hoá
    public float Rx = 0.3f, Ry = 0.3f;  // bán kính chuẩn hoá
    public float Feather = 0.4f;        // [0..1]
    public bool Invert;                 // true = áp trong elip (như LR "inside")

    public float[] Generate(int width, int height)
    {
        var m = new float[width * height];
        float feather = Math.Clamp(Feather, 0.01f, 1f);
        Parallel.For(0, height, y =>
        {
            float ny = height <= 1 ? 0f : y / (float)(height - 1);
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float nx = width <= 1 ? 0f : x / (float)(width - 1);
                float ddx = (nx - Cx) / MathF.Max(1e-4f, Rx);
                float ddy = (ny - Cy) / MathF.Max(1e-4f, Ry);
                float d = MathF.Sqrt(ddx * ddx + ddy * ddy); // 1 = mép elip
                // mặc định: hiệu ứng NGOÀI elip (như graduated radial). t=0 trong, 1 ngoài.
                float t = (d - (1f - feather)) / feather;
                t = Math.Clamp(t, 0f, 1f);
                float s = t * t * (3f - 2f * t);
                m[row + x] = Invert ? 1f - s : s;
            }
        });
        return m;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mask"] = Type, ["cx"] = F(Cx), ["cy"] = F(Cy), ["rx"] = F(Rx), ["ry"] = F(Ry),
        ["feather"] = F(Feather), ["invert"] = Invert ? "true" : "false",
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static RadialMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Cx = EditOpRegistry.F(p, "cx", 0.5f), Cy = EditOpRegistry.F(p, "cy", 0.5f),
        Rx = EditOpRegistry.F(p, "rx", 0.3f), Ry = EditOpRegistry.F(p, "ry", 0.3f),
        Feather = EditOpRegistry.F(p, "feather", 0.4f), Invert = EditOpRegistry.B(p, "invert"),
    };
}

/// <summary>
/// Range mask theo luminance: chỉ áp ở vùng có độ sáng trong [Min,Max] (perceptual),
/// mép mượt theo Smoothness. Dùng kết hợp (nhân) với mask hình học hoặc đứng riêng.
/// </summary>
public sealed class LuminanceRangeMask : IMaskGenerator
{
    public const string Type = "LumRange";
    public string MaskType => Type;
    public float Min = 0f, Max = 1f, Smooth = 0.1f;

    public float[] Generate(int width, int height) => throw new NotSupportedException("Range mask cần ảnh; dùng GenerateFrom.");

    /// <summary>Range mask phải sinh từ pixel ảnh.</summary>
    public float[] GenerateFrom(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var m = new float[w * h];
        float[] px = img.Pixels;
        float lo = Math.Clamp(Min, 0f, 1f), hi = Math.Clamp(Max, 0f, 1f);
        float sm = MathF.Max(1e-3f, Smooth);
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float L = ColorSpace.LinearToSrgb(ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]));
                float up = Smoothstep(lo - sm, lo + sm, L);
                float down = 1f - Smoothstep(hi - sm, hi + sm, L);
                m[row + x] = Math.Clamp(up * down, 0f, 1f);
            }
        });
        return m;
    }

    private static float Smoothstep(float e0, float e1, float x)
    {
        if (e1 <= e0) return x >= e1 ? 1f : 0f;
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mask"] = Type, ["min"] = F(Min), ["max"] = F(Max), ["smooth"] = F(Smooth),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static LuminanceRangeMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Min = EditOpRegistry.F(p, "min"), Max = EditOpRegistry.F(p, "max", 1f), Smooth = EditOpRegistry.F(p, "smooth", 0.1f),
    };
}

/// <summary>
/// Range mask theo MÀU: chỉ áp ở vùng có hue gần TargetHue (trong khoảng ±HueRange trên vòng
/// tròn màu) và đủ bão hoà (S &gt;= MinSat). Mép mượt theo Smooth. Dùng để chọn "bầu trời xanh",
/// "da", "cây xanh"... mà không cần AI. Cần pixel ảnh nên sinh qua GenerateFrom (như LumRange).
/// </summary>
public sealed class ColorRangeMask : IMaskGenerator
{
    public const string Type = "ColorRange";
    public string MaskType => Type;

    public float TargetHue;       // độ [0..360]
    public float HueRange = 30f;  // nửa độ rộng cửa sổ hue (độ)
    public float MinSat = 0.1f;   // [0..1] ngưỡng bão hoà tối thiểu
    public float Smooth = 0.2f;   // [0..1] độ mượt mép (theo tỉ lệ HueRange + sat)

    public float[] Generate(int width, int height) => throw new NotSupportedException("Color range mask cần ảnh; dùng GenerateFrom.");

    public float[] GenerateFrom(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var m = new float[w * h];
        float[] px = img.Pixels;
        float tH = ((TargetHue % 360f) + 360f) % 360f;
        float range = MathF.Max(1f, HueRange);
        float sm = Math.Clamp(Smooth, 0f, 1f);
        float hueSoft = range * (0.25f + sm);      // mép mềm hue (độ)
        float minSat = Math.Clamp(MinSat, 0f, 1f);
        float satSoft = MathF.Max(1e-3f, sm * 0.5f);

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float sr = ColorSpace.LinearToSrgb(px[p]);
                float sg = ColorSpace.LinearToSrgb(px[p + 1]);
                float sb = ColorSpace.LinearToSrgb(px[p + 2]);
                RgbToHsv(sr, sg, sb, out float hue, out float sat, out _);

                // khoảng cách hue trên vòng tròn.
                float d = MathF.Abs(hue - tH);
                if (d > 180f) d = 360f - d;
                // 1 trong cửa sổ [0..range], giảm dần tới [range+hueSoft].
                float hueW = 1f - Smoothstep(range, range + hueSoft, d);
                // sat gate: dưới minSat -> 0, mượt lên trên.
                float satW = Smoothstep(minSat - satSoft, minSat + satSoft, sat);
                m[row + x] = Math.Clamp(hueW * satW, 0f, 1f);
            }
        });
        return m;
    }

    private static float Smoothstep(float e0, float e1, float x)
    {
        if (e1 <= e0) return x >= e1 ? 1f : 0f;
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        v = max; float d = max - min; s = max > 1e-6f ? d / max : 0f;
        if (d < 1e-6f) { h = 0f; return; }
        if (max == r) h = 60f * (((g - b) / d) % 6f);
        else if (max == g) h = 60f * (((b - r) / d) + 2f);
        else h = 60f * (((r - g) / d) + 4f);
        if (h < 0f) h += 360f;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mask"] = Type, ["hue"] = F(TargetHue), ["range"] = F(HueRange), ["minSat"] = F(MinSat), ["smooth"] = F(Smooth),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static ColorRangeMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        TargetHue = EditOpRegistry.F(p, "hue"),
        HueRange = EditOpRegistry.F(p, "range", 30f),
        MinSat = EditOpRegistry.F(p, "minSat", 0.1f),
        Smooth = EditOpRegistry.F(p, "smooth", 0.2f),
    };
}

/// <summary>
/// Brush mask: vẽ tay bằng chuỗi "chấm" (stroke points) toạ độ chuẩn hoá [0..1], mỗi chấm là
/// 1 đĩa mềm bán kính Radius (chuẩn hoá theo cạnh dài) với Hardness điều khiển độ cứng mép.
/// Mask = hợp (max) của tất cả chấm -> mô phỏng nét cọ liên tục. Erase=true thì là vùng xoá
/// (xử lý ở builder bằng cách trừ). Toạ độ chuẩn hoá nên khớp proxy/full-res.
///
/// Serialize: các điểm gói thành "pts" = "x0,y0;x1,y1;...".
/// </summary>
public sealed class BrushMask : IMaskGenerator
{
    public const string Type = "Brush";
    public string MaskType => Type;

    public List<(float X, float Y)> Points = new();
    public float Radius = 0.05f;   // chuẩn hoá theo max(W,H)
    public float Hardness = 0.5f;  // [0..1] 1 = mép cứng

    public float[] Generate(int width, int height)
    {
        var m = new float[width * height];
        if (Points.Count == 0) return m;
        float maxEdge = MathF.Max(width, height);
        float rad = MathF.Max(1f, Radius * maxEdge);   // px
        float hard = Math.Clamp(Hardness, 0f, 0.99f);
        float inner = rad * hard;                       // bán kính lõi đầy đủ
        float aspectX = width, aspectY = height;

        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float best = 0f;
                foreach (var pt in Points)
                {
                    float px = pt.X * (aspectX - 1);
                    float py = pt.Y * (aspectY - 1);
                    float dx = x - px, dy = y - py;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float v;
                    if (dist <= inner) v = 1f;
                    else if (dist >= rad) v = 0f;
                    else
                    {
                        float t = 1f - (dist - inner) / MathF.Max(1e-3f, rad - inner);
                        v = t * t * (3f - 2f * t);
                    }
                    if (v > best) best = v;
                    if (best >= 1f) break;
                }
                m[row + x] = best;
            }
        });
        return m;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mask"] = Type, ["radius"] = F(Radius), ["hardness"] = F(Hardness), ["pts"] = PackPoints(Points),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static BrushMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Radius = EditOpRegistry.F(p, "radius", 0.05f),
        Hardness = EditOpRegistry.F(p, "hardness", 0.5f),
        Points = UnpackPoints(EditOpRegistry.S(p, "pts")),
    };

    internal static string PackPoints(List<(float X, float Y)> pts)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(pts[i].X.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(pts[i].Y.ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    internal static List<(float X, float Y)> UnpackPoints(string s)
    {
        var list = new List<(float, float)>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (var pair in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = pair.Split(',');
            if (xy.Length != 2) continue;
            if (float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                list.Add((x, y));
        }
        return list;
    }
}
