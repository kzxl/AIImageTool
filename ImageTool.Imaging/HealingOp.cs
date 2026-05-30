using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Healing / Clone brush (#6) — xoá vết bụi/khuyết điểm bằng cách chép pixel từ vùng NGUỒN sang
/// vùng ĐÍCH với mép mềm. Mỗi "spot" gồm: tâm đích (tx,ty), tâm nguồn (sx,sy), bán kính (r) — tất cả
/// toạ độ CHUẨN HOÁ [0..1] theo cạnh để khớp proxy/full-res.
///
/// 2 chế độ:
///  - Clone: chép thẳng pixel nguồn (giữ nguyên texture nguồn).
///  - Heal: chép nguồn rồi hiệu chỉnh để khớp độ sáng/màu trung bình vùng đích (vá liền mạch hơn).
///
/// Serialize: "spots" = "tx,ty,sx,sy,r;..." + "mode" = clone|heal.
/// </summary>
public sealed class HealingOp : IEditOp
{
    public const string Type = "Healing";
    public string OpType => Type;

    public enum HealMode { Clone, Heal }

    public List<Spot> Spots = new();
    public HealMode Mode = HealMode.Heal;

    public readonly record struct Spot(float Tx, float Ty, float Sx, float Sy, float Radius);

    public bool IsIdentity => Spots.Count == 0;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float maxEdge = MathF.Max(w, h);
        // Đọc 1 bản snapshot nguồn bất biến để các spot không "ăn" kết quả của nhau.
        float[] src = (float[])image.Pixels.Clone();
        float[] dst = image.Pixels;

        foreach (var s in Spots)
        {
            int radPx = Math.Max(1, (int)MathF.Round(s.Radius * maxEdge));
            int tcx = (int)MathF.Round(s.Tx * (w - 1));
            int tcy = (int)MathF.Round(s.Ty * (h - 1));
            int scx = (int)MathF.Round(s.Sx * (w - 1));
            int scy = (int)MathF.Round(s.Sy * (h - 1));

            // Heal: bù để vùng vá khớp với VIỀN XUNG QUANH đích (vùng "sạch"), không lấy chính vết.
            float offR = 0, offG = 0, offB = 0;
            if (Mode == HealMode.Heal)
                ComputeRingOffset(src, w, h, tcx, tcy, scx, scy, radPx, out offR, out offG, out offB);

            ApplySpot(src, dst, w, h, tcx, tcy, scx, scy, radPx, offR, offG, offB);
        }
    }

    // Lệch màu = (trung bình VIỀN quanh đích, từ rad..rad*1.6) - (trung bình ĐĨA nguồn).
    // Lấy viền ngoài vết (vùng tốt) để vá liền mạch, tránh tái tạo lại khuyết điểm.
    private static void ComputeRingOffset(float[] src, int w, int h, int tcx, int tcy, int scx, int scy, int rad,
        out float offR, out float offG, out float offB)
    {
        int outer = (int)MathF.Ceiling(rad * 1.6f);
        double tr = 0, tg = 0, tb = 0; int tn = 0;
        for (int dy = -outer; dy <= outer; dy++)
            for (int dx = -outer; dx <= outer; dx++)
            {
                float d2 = dx * dx + dy * dy;
                if (d2 <= rad * rad || d2 > outer * outer) continue; // chỉ lấy viền
                int tx = tcx + dx, ty = tcy + dy;
                if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                int to = (ty * w + tx) * 4;
                tr += src[to]; tg += src[to + 1]; tb += src[to + 2]; tn++;
            }

        double sr = 0, sg = 0, sb = 0; int sn = 0;
        for (int dy = -rad; dy <= rad; dy++)
            for (int dx = -rad; dx <= rad; dx++)
            {
                if (dx * dx + dy * dy > rad * rad) continue;
                int sx = scx + dx, sy = scy + dy;
                if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                int so = (sy * w + sx) * 4;
                sr += src[so]; sg += src[so + 1]; sb += src[so + 2]; sn++;
            }

        if (tn > 0 && sn > 0)
        {
            offR = (float)(tr / tn - sr / sn);
            offG = (float)(tg / tn - sg / sn);
            offB = (float)(tb / tn - sb / sn);
        }
        else { offR = offG = offB = 0; }
    }

    private static void ApplySpot(float[] src, float[] dst, int w, int h, int tcx, int tcy, int scx, int scy, int rad,
        float offR, float offG, float offB)
    {
        float radF = rad;
        for (int dy = -rad; dy <= rad; dy++)
        {
            int ty = tcy + dy, sy = scy + dy;
            if (ty < 0 || ty >= h) continue;
            for (int dx = -rad; dx <= rad; dx++)
            {
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > radF) continue;
                int tx = tcx + dx, sx = scx + dx;
                if (tx < 0 || tx >= w) continue;
                if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;

                // feather: 1 ở tâm, 0 ở mép (smoothstep theo dist/rad).
                float tnorm = dist / radF;
                float feather = 1f - (tnorm * tnorm * (3f - 2f * tnorm));
                int to = (ty * w + tx) * 4, so = (sy * w + sx) * 4;
                float nr = src[so] + offR, ng = src[so + 1] + offG, nb = src[so + 2] + offB;
                if (nr < 0) nr = 0; if (ng < 0) ng = 0; if (nb < 0) nb = 0;
                dst[to] = src[to] + (nr - src[to]) * feather;
                dst[to + 1] = src[to + 1] + (ng - src[to + 1]) * feather;
                dst[to + 2] = src[to + 2] + (nb - src[to + 2]) * feather;
            }
        }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["spots"] = PackSpots(Spots),
        ["mode"] = Mode == HealMode.Clone ? "clone" : "heal",
    };

    public static HealingOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Spots = UnpackSpots(EditOpRegistry.S(p, "spots")),
        Mode = EditOpRegistry.S(p, "mode") == "clone" ? HealMode.Clone : HealMode.Heal,
    };

    internal static string PackSpots(List<Spot> spots)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < spots.Count; i++)
        {
            if (i > 0) sb.Append(';');
            var s = spots[i];
            sb.Append(F(s.Tx)).Append(',').Append(F(s.Ty)).Append(',')
              .Append(F(s.Sx)).Append(',').Append(F(s.Sy)).Append(',').Append(F(s.Radius));
        }
        return sb.ToString();
    }

    internal static List<Spot> UnpackSpots(string s)
    {
        var list = new List<Spot>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = part.Split(',');
            if (c.Length != 5) continue;
            if (P(c[0], out var tx) && P(c[1], out var ty) && P(c[2], out var sx) && P(c[3], out var sy) && P(c[4], out var r))
                list.Add(new Spot(tx, ty, sx, sy, r));
        }
        return list;
    }

    private static bool P(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
