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

/// <summary>
/// Parametric mask đa kênh (D4.1, kiểu Darktable "parametric masking"): chọn vùng theo NHIỀU kênh
/// cùng lúc — L (lightness), C (chroma), H (hue) trong Lab/HSV và R, G, B (sRGB). Mỗi kênh là 1
/// band-pass [Min..Max] (giá trị chuẩn hoá [0..1], hue cũng [0..1] = độ/360) với mép mượt theo Feather.
/// Kênh "không giới hạn" (Min≈0 và Max≈1) không ràng buộc gì. Mask cuối = TÍCH trọng số của các kênh
/// đang giới hạn — giao của tất cả điều kiện, đúng tinh thần parametric mask. Hue hỗ trợ wrap
/// (Min &gt; Max = chọn dải vòng qua 0/360, vd đỏ). Cần pixel ảnh nên sinh qua GenerateFrom.
///
/// Serialize: "lMin/lMax/lFeather", "cMin/cMax/cFeather", "hMin/hMax/hFeather",
/// "rMin/rMax/rFeather", "gMin/gMax/gFeather", "bMin/bMax/bFeather", "invert".
/// </summary>
public sealed class ParametricMask : IMaskGenerator
{
    public const string Type = "Parametric";
    public string MaskType => Type;

    // 6 kênh: L, C, H, R, G, B. Mỗi kênh band-pass [Min..Max] + Feather, đều chuẩn hoá [0..1].
    public float LMin = 0f, LMax = 1f, LFeather = 0.1f;
    public float CMin = 0f, CMax = 1f, CFeather = 0.1f;
    public float HMin = 0f, HMax = 1f, HFeather = 0.1f;
    public float RMin = 0f, RMax = 1f, RFeather = 0.1f;
    public float GMin = 0f, GMax = 1f, GFeather = 0.1f;
    public float BMin = 0f, BMax = 1f, BFeather = 0.1f;
    public bool Invert;

    // Chuẩn hoá chroma Lab về [0..1] (C* tối đa thực tế ~132 cho màu rực sRGB).
    private const float ChromaScale = 1f / 132f;

    public float[] Generate(int width, int height) => throw new NotSupportedException("Parametric mask cần ảnh; dùng GenerateFrom.");

    /// <summary>1 kênh có thực sự giới hạn không (nếu không thì bỏ qua khi nhân).</summary>
    private static bool Active(float min, float max) => min > 1e-3f || max < 1f - 1e-3f;

    public float[] GenerateFrom(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var m = new float[w * h];
        float[] px = img.Pixels;

        bool useL = Active(LMin, LMax), useC = Active(CMin, CMax), useH = Active(HMin, HMax);
        bool useR = Active(RMin, RMax), useG = Active(GMin, GMax), useB = Active(BMin, BMax);
        bool needLab = useL || useC;
        bool any = useL || useC || useH || useR || useG || useB;
        bool invert = Invert;

        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float r = px[p], g = px[p + 1], b = px[p + 2];
                float sr = ColorSpace.LinearToSrgb(r), sg = ColorSpace.LinearToSrgb(g), sb = ColorSpace.LinearToSrgb(b);

                float v = 1f;
                if (!any) { m[row + x] = invert ? 0f : 1f; continue; }

                if (needLab)
                {
                    RgbToLab(r, g, b, out float L, out float aa, out float bbLab);
                    if (useL) v *= Band(Math.Clamp(L / 100f, 0f, 1f), LMin, LMax, LFeather);
                    if (useC && v > 0f)
                    {
                        float chroma = MathF.Sqrt(aa * aa + bbLab * bbLab) * ChromaScale;
                        v *= Band(Math.Clamp(chroma, 0f, 1f), CMin, CMax, CFeather);
                    }
                }
                if (useH && v > 0f)
                {
                    RgbToHsv(sr, sg, sb, out float hue, out _, out _);
                    v *= BandHue(hue / 360f, HMin, HMax, HFeather);
                }
                if (useR && v > 0f) v *= Band(sr, RMin, RMax, RFeather);
                if (useG && v > 0f) v *= Band(sg, GMin, GMax, GFeather);
                if (useB && v > 0f) v *= Band(sb, BMin, BMax, BFeather);

                v = Math.Clamp(v, 0f, 1f);
                m[row + x] = invert ? 1f - v : v;
            }
        });
        return m;
    }

    /// <summary>Band-pass: full (1) trong [min,max], giảm mượt RA NGOÀI theo feather
    /// (plateau = [min,max], falloff trên [min-f..min] và [max..max+f]). Khớp Darktable parametric.</summary>
    private static float Band(float xv, float min, float max, float feather)
    {
        float f = MathF.Max(1e-4f, feather);
        float rising = Smoothstep(min - f, min, xv);    // 0 tại min-f -> 1 tại min
        float falling = 1f - Smoothstep(max, max + f, xv); // 1 tại max -> 0 tại max+f
        return rising * falling;
    }

    /// <summary>Band-pass cho hue [0..1] có WRAP: nếu min &gt; max thì chọn dải vòng qua 0/1.</summary>
    private static float BandHue(float xv, float min, float max, float feather)
    {
        if (min <= max) return Band(xv, min, max, feather);
        // wrap: [min..1] hợp [0..max] -> lấy max của 2 band.
        float a = Band(xv, min, 1f, feather);
        float b = Band(xv, 0f, max, feather);
        return MathF.Max(a, b);
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

    // Lab (D65) từ linear RGB — cùng công thức ColorContrastOp.
    private static void RgbToLab(float r, float g, float b, out float L, out float a, out float bb)
    {
        float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
        float yy = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
        float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;
        x /= 0.95047f; z /= 1.08883f;
        float fx = LabF(x), fy = LabF(yy), fz = LabF(z);
        L = 116f * fy - 16f;
        a = 500f * (fx - fy);
        bb = 200f * (fy - fz);
    }
    private static float LabF(float t)
    {
        const float d = 6f / 29f;
        return t > d * d * d ? MathF.Cbrt(t) : t / (3f * d * d) + 4f / 29f;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mask"] = Type,
        ["lMin"] = F(LMin), ["lMax"] = F(LMax), ["lFeather"] = F(LFeather),
        ["cMin"] = F(CMin), ["cMax"] = F(CMax), ["cFeather"] = F(CFeather),
        ["hMin"] = F(HMin), ["hMax"] = F(HMax), ["hFeather"] = F(HFeather),
        ["rMin"] = F(RMin), ["rMax"] = F(RMax), ["rFeather"] = F(RFeather),
        ["gMin"] = F(GMin), ["gMax"] = F(GMax), ["gFeather"] = F(GFeather),
        ["bMin"] = F(BMin), ["bMax"] = F(BMax), ["bFeather"] = F(BFeather),
        ["invert"] = Invert ? "true" : "false",
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static ParametricMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        LMin = EditOpRegistry.F(p, "lMin"), LMax = EditOpRegistry.F(p, "lMax", 1f), LFeather = EditOpRegistry.F(p, "lFeather", 0.1f),
        CMin = EditOpRegistry.F(p, "cMin"), CMax = EditOpRegistry.F(p, "cMax", 1f), CFeather = EditOpRegistry.F(p, "cFeather", 0.1f),
        HMin = EditOpRegistry.F(p, "hMin"), HMax = EditOpRegistry.F(p, "hMax", 1f), HFeather = EditOpRegistry.F(p, "hFeather", 0.1f),
        RMin = EditOpRegistry.F(p, "rMin"), RMax = EditOpRegistry.F(p, "rMax", 1f), RFeather = EditOpRegistry.F(p, "rFeather", 0.1f),
        GMin = EditOpRegistry.F(p, "gMin"), GMax = EditOpRegistry.F(p, "gMax", 1f), GFeather = EditOpRegistry.F(p, "gFeather", 0.1f),
        BMin = EditOpRegistry.F(p, "bMin"), BMax = EditOpRegistry.F(p, "bMax", 1f), BFeather = EditOpRegistry.F(p, "bFeather", 0.1f),
        Invert = EditOpRegistry.B(p, "invert"),
    };
}

/// <summary>
/// Sky mask heuristic (#1) — chọn vùng bầu trời KHÔNG cần AI: chấm điểm mỗi pixel theo (a) độ "xanh
/// trời" (B nổi trội + hue lam-lơ) hoặc rất sáng (mây trắng), (b) vị trí dọc (trời thường ở nửa trên).
/// Đủ tốt cho ảnh phong cảnh phổ biến; ảnh phức tạp dùng AI Subject + invert. Cần pixel ảnh nên
/// sinh qua GenerateFrom (như LuminanceRange/ColorRange).
///
/// Tham số: Strength [0..1] (độ mạnh ưu tiên vị trí trên), Smooth [0..1] (mềm mép).
/// </summary>
public sealed class SkyMask : IMaskGenerator
{
    public const string Type = "Sky";
    public string MaskType => Type;

    public float Strength = 0.7f;
    public float Smooth = 0.15f;

    public float[] Generate(int width, int height) => throw new NotSupportedException("Sky mask cần ảnh; dùng GenerateFrom.");

    public float[] GenerateFrom(LinearImage img)
    {
        int w = img.Width, h = img.Height;
        var m = new float[w * h];
        float[] px = img.Pixels;
        float strength = Math.Clamp(Strength, 0f, 1f);
        float sm = Math.Max(1e-3f, Smooth);

        Parallel.For(0, h, y =>
        {
            // ưu tiên vị trí: trên = 1, dưới giảm dần (kết hợp strength).
            float ny = h <= 1 ? 0f : y / (float)(h - 1);
            float vert = 1f - Smoothstep(0.45f, 0.45f + 0.4f, ny); // mềm quanh đường chân trời ~giữa
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float r = px[p], g = px[p + 1], b = px[p + 2];
                float lum = ColorSpace.LinearToSrgb(ColorSpace.Luminance(r, g, b));
                float sr = ColorSpace.LinearToSrgb(r), sg = ColorSpace.LinearToSrgb(g), sb = ColorSpace.LinearToSrgb(b);

                // điểm "xanh trời": B vượt R, và không quá tối.
                float blueScore = Math.Clamp((sb - sr) * 2.0f, 0f, 1f) * Smoothstep(0.2f, 0.4f, lum);
                // điểm "mây/trời sáng": rất sáng + ít bão hoà.
                float maxc = MathF.Max(sr, MathF.Max(sg, sb)), minc = MathF.Min(sr, MathF.Min(sg, sb));
                float sat = maxc > 1e-4f ? (maxc - minc) / maxc : 0f;
                float brightScore = Smoothstep(0.7f, 0.92f, lum) * (1f - Smoothstep(0.25f, 0.5f, sat));

                float colorScore = MathF.Max(blueScore, brightScore);
                // kết hợp màu × vị trí (strength điều khiển mức phụ thuộc vị trí).
                float vertW = (1f - strength) + strength * vert;
                float v = colorScore * vertW;
                // mềm hoá biên.
                v = Smoothstep(0f, sm, v) * v;
                m[row + x] = Math.Clamp(v, 0f, 1f);
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
        ["mask"] = Type, ["strength"] = F(Strength), ["smooth"] = F(Smooth),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static SkyMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Strength = EditOpRegistry.F(p, "strength", 0.7f),
        Smooth = EditOpRegistry.F(p, "smooth", 0.15f),
    };
}
