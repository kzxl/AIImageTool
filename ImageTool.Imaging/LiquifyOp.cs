using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Liquify / Warp (D3.5 Darktable "retouch"/liquify cơ bản): biến dạng cục bộ ảnh bằng tập "handle"
/// đẩy/kéo. Mỗi handle là 1 điểm tâm (Cx,Cy) cùng 1 vector dịch (Dx,Dy) trong bán kính Radius:
/// nội dung quanh tâm bị đẩy theo vector, giảm dần ra mép theo hàm falloff mượt (1 - t²)². Nhiều
/// handle cộng dồn trường dịch chuyển.
///
/// Là <see cref="IResizingOp"/> (giữ nguyên W×H, remap pixel). Toạ độ/bán kính/dịch đều CHUẨN HOÁ
/// nên khớp giữa proxy và full-res (op tự dùng W×H hiện tại). Lấy mẫu NGƯỢC: với mỗi pixel đích P,
/// giải gần đúng nguồn S sao cho S + disp(S) = P bằng lặp điểm-bất-động vài vòng, rồi nội suy song
/// tuyến. Ngoài biên kẹp về mép (không tạo lỗ thủng).
/// </summary>
public sealed class LiquifyOp : IResizingOp
{
    public const string Type = "Liquify";
    public string OpType => Type;

    /// <summary>1 handle đẩy/kéo. Toạ độ/độ dài CHUẨN HOÁ.</summary>
    public struct Warp
    {
        public float Cx, Cy;   // tâm handle [0..1]
        public float Dx, Dy;   // vector dịch (theo đơn vị cạnh dài) — nội dung tại tâm xuất hiện ở tâm + (Dx,Dy)
        public float Radius;   // bán kính ảnh hưởng (theo cạnh dài), >0
    }

    public List<Warp> Warps = new();

    /// <summary>Số vòng lặp điểm-bất-động khi nghịch đảo trường dịch (2..4 đủ cho warp vừa).</summary>
    public int Iterations = 3;

    public bool IsIdentity
    {
        get
        {
            if (Warps.Count == 0) return true;
            foreach (var wpt in Warps)
                if (wpt.Radius > 1e-5f && (MathF.Abs(wpt.Dx) > 1e-6f || MathF.Abs(wpt.Dy) > 1e-6f))
                    return false;
            return true;
        }
    }

    public void Apply(LinearImage image, float scale) { /* resizing op */ }

    public LinearImage ApplyResize(LinearImage img, float scale)
    {
        if (IsIdentity) return img;
        int w = img.Width, h = img.Height;
        var dst = new LinearImage(w, h);
        float[] s = img.Pixels, d = dst.Pixels;

        // Chuyển handle sang pixel-space của ảnh hiện tại. Tâm theo từng trục (w-1, h-1);
        // dịch & bán kính theo cạnh dài để đẳng hướng (không méo theo tỉ lệ ảnh).
        float ax = w - 1, ay = h - 1;
        float diag = MathF.Max(ax, ay);
        if (diag < 1f) return img;

        int n = Warps.Count;
        var cx = new float[n]; var cy = new float[n];
        var vx = new float[n]; var vy = new float[n];
        var r2 = new float[n];
        for (int i = 0; i < n; i++)
        {
            var wp = Warps[i];
            cx[i] = wp.Cx * ax;
            cy[i] = wp.Cy * ay;
            vx[i] = wp.Dx * diag;
            vy[i] = wp.Dy * diag;
            float r = MathF.Max(wp.Radius * diag, 1f);
            r2[i] = r * r;
        }

        int iters = Math.Clamp(Iterations, 1, 8);

        Parallel.For(0, h, dy =>
        {
            for (int dx = 0; dx < w; dx++)
            {
                // Nghịch đảo trường dịch: tìm nguồn (sxp,syp) với (sxp+disp) = đích.
                // Bắt đầu từ đích, lặp: src = dest - disp(src).
                float sxp = dx, syp = dy;
                for (int it = 0; it < iters; it++)
                {
                    float ddx = 0f, ddy = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float ex = sxp - cx[i], ey = syp - cy[i];
                        float dist2 = ex * ex + ey * ey;
                        if (dist2 >= r2[i]) continue;
                        float t2 = dist2 / r2[i];          // [0..1)
                        float fall = 1f - t2;              // (1 - t²)²  -> bump mượt, đạo hàm 0 tại mép
                        fall *= fall;
                        ddx += vx[i] * fall;
                        ddy += vy[i] * fall;
                    }
                    sxp = dx - ddx;
                    syp = dy - ddy;
                }
                Sample(s, w, h, sxp, syp, d, (dy * w + dx) * 4);
            }
        });
        return dst;
    }

    // Lấy mẫu song tuyến, kẹp toạ độ về mép (không tạo lỗ thủng/trong suốt).
    private static void Sample(float[] s, int sw, int sh, float fx, float fy, float[] d, int o)
    {
        if (fx < 0f) fx = 0f; else if (fx > sw - 1) fx = sw - 1;
        if (fy < 0f) fy = 0f; else if (fy > sh - 1) fy = sh - 1;
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(sw - 1, x0 + 1), y1 = Math.Min(sh - 1, y0 + 1);
        float tx = fx - x0, ty = fy - y0;
        for (int c = 0; c < 4; c++)
        {
            float p00 = s[(y0 * sw + x0) * 4 + c];
            float p10 = s[(y0 * sw + x1) * 4 + c];
            float p01 = s[(y1 * sw + x0) * 4 + c];
            float p11 = s[(y1 * sw + x1) * 4 + c];
            float top = p00 + (p10 - p00) * tx;
            float bot = p01 + (p11 - p01) * tx;
            d[o + c] = top + (bot - top) * ty;
        }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["warps"] = PackWarps(Warps),
        ["iters"] = Iterations.ToString(CultureInfo.InvariantCulture),
    };

    public static LiquifyOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Warps = UnpackWarps(EditOpRegistry.S(p, "warps")),
        Iterations = EditOpRegistry.I(p, "iters", 3),
    };

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);

    // --- (de)serialize danh sách handle: "cx,cy,dx,dy,r" nối bằng ';' ---
    internal static string PackWarps(List<Warp> warps)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < warps.Count; i++)
        {
            if (i > 0) sb.Append(';');
            var wp = warps[i];
            sb.Append(F(wp.Cx)).Append(',').Append(F(wp.Cy)).Append(',')
              .Append(F(wp.Dx)).Append(',').Append(F(wp.Dy)).Append(',').Append(F(wp.Radius));
        }
        return sb.ToString();
    }

    internal static List<Warp> UnpackWarps(string s)
    {
        var list = new List<Warp>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (var item in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = item.Split(',');
            if (f.Length != 5) continue;
            if (P(f[0], out var cx) && P(f[1], out var cy) && P(f[2], out var dx) &&
                P(f[3], out var dy) && P(f[4], out var r))
                list.Add(new Warp { Cx = cx, Cy = cy, Dx = dx, Dy = dy, Radius = r });
        }
        return list;
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static bool P(string s, out float v)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
