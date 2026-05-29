using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Toán đường cong dùng chung cho cả <see cref="ToneCurveOp"/> (áp lên ảnh) và UI curve editor
/// (vẽ + kéo điểm). Tách ra để op và editor render GIỐNG HỆT nhau (cùng spline monotone-cubic),
/// và để test độc lập.
///
/// Điểm điều khiển ở không gian [0..1] x [0..1]. Nội suy Fritsch-Carlson monotone cubic
/// (không overshoot). Sinh LUT 256 mức tra cứu O(1).
/// </summary>
public static class CurveMath
{
    public const int LutSize = 256;

    /// <summary>Chuẩn hoá danh sách điểm: clamp [0..1], sắp theo x, đảm bảo ≥2 điểm.</summary>
    public static List<(float x, float y)> Normalize(IReadOnlyList<(float x, float y)>? points)
    {
        var list = new List<(float x, float y)>();
        if (points != null)
            foreach (var p in points)
                list.Add((Math.Clamp(p.x, 0f, 1f), Math.Clamp(p.y, 0f, 1f)));
        if (list.Count < 2) return new() { (0f, 0f), (1f, 1f) };
        list.Sort((a, b) => a.x.CompareTo(b.x));
        return list;
    }

    /// <summary>True nếu cong là đường chéo identity (0,0)->(1,1).</summary>
    public static bool IsIdentity(IReadOnlyList<(float x, float y)> normalized)
        => normalized.Count == 2 &&
           Near(normalized[0].x, 0) && Near(normalized[0].y, 0) &&
           Near(normalized[1].x, 1) && Near(normalized[1].y, 1);

    /// <summary>Dựng LUT 256 mức từ các điểm (đã hoặc chưa chuẩn hoá).</summary>
    public static float[] BuildLut(IReadOnlyList<(float x, float y)>? points)
    {
        var pts = Normalize(points);
        int n = pts.Count;
        var xs = new float[n]; var ys = new float[n];
        for (int i = 0; i < n; i++) { xs[i] = pts[i].x; ys[i] = pts[i].y; }

        var d = new float[n - 1];
        var m = new float[n];
        for (int i = 0; i < n - 1; i++)
        {
            float dx = xs[i + 1] - xs[i];
            d[i] = dx > 1e-6f ? (ys[i + 1] - ys[i]) / dx : 0f;
        }
        m[0] = d[0];
        m[n - 1] = d[n - 2];
        for (int i = 1; i < n - 1; i++)
            m[i] = (d[i - 1] * d[i] <= 0f) ? 0f : (d[i - 1] + d[i]) / 2f;
        for (int i = 0; i < n - 1; i++)
        {
            if (Near(d[i], 0f)) { m[i] = 0f; m[i + 1] = 0f; continue; }
            float a = m[i] / d[i];
            float b = m[i + 1] / d[i];
            float hyp = a * a + b * b;
            if (hyp > 9f)
            {
                float t = 3f / MathF.Sqrt(hyp);
                m[i] = t * a * d[i];
                m[i + 1] = t * b * d[i];
            }
        }

        var lut = new float[LutSize];
        int seg = 0;
        for (int k = 0; k < LutSize; k++)
        {
            float x = k / (float)(LutSize - 1);
            while (seg < n - 2 && x > xs[seg + 1]) seg++;
            float x0 = xs[seg], x1 = xs[seg + 1];
            float h = x1 - x0;
            float y;
            if (h <= 1e-6f) y = ys[seg];
            else
            {
                float t = (x - x0) / h;
                float t2 = t * t, t3 = t2 * t;
                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + t;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;
                y = h00 * ys[seg] + h10 * h * m[seg] + h01 * ys[seg + 1] + h11 * h * m[seg + 1];
            }
            lut[k] = Math.Clamp(y, 0f, 1f);
        }
        return lut;
    }

    /// <summary>Đánh giá curve tại x [0..1] qua LUT (nội suy tuyến tính giữa 2 mức).</summary>
    public static float Eval(float[] lut, float x)
    {
        if (x <= 0f) return lut[0];
        if (x >= 1f) return lut[LutSize - 1];
        float fx = x * (LutSize - 1);
        int i = (int)fx;
        float frac = fx - i;
        if (i >= LutSize - 1) return lut[LutSize - 1];
        return lut[i] + (lut[i + 1] - lut[i]) * frac;
    }

    /// <summary>Serialize "x0,y0;x1,y1;..." (culture-invariant).</summary>
    public static string Serialize(IReadOnlyList<(float x, float y)> points)
    {
        var pts = Normalize(points);
        var parts = new string[pts.Count];
        for (int i = 0; i < pts.Count; i++)
            parts[i] = $"{pts[i].x.ToString("R", CultureInfo.InvariantCulture)},{pts[i].y.ToString("R", CultureInfo.InvariantCulture)}";
        return string.Join(';', parts);
    }

    /// <summary>Parse "x0,y0;x1,y1;..." -> điểm; null nếu &lt;2 điểm hợp lệ.</summary>
    public static List<(float x, float y)>? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var list = new List<(float x, float y)>();
        foreach (var pair in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = pair.Split(',');
            if (xy.Length != 2) continue;
            if (float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                list.Add((x, y));
        }
        return list.Count >= 2 ? list : null;
    }

    private static bool Near(float a, float b = 0f) => MathF.Abs(a - b) < 1e-4f;
}
