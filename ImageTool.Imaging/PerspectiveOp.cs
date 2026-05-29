using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Perspective / Upright (keystone): hiệu chỉnh phối cảnh bằng phép biến đổi đồng nhất
/// (homography 3x3). Hai tham số trực quan Vertical / Horizontal mô phỏng nghiêng máy
/// (giống Upright của Lightroom): Vertical &gt; 0 kéo đỉnh ra (sửa nhà bị "ngả sau"),
/// Horizontal xoay quanh trục dọc. Rotate xoay phẳng, Scale phóng để bù viền đen.
///
/// Là IResizingOp (giữ nguyên W×H nhưng remap pixel). Toạ độ chuẩn hoá nên khớp proxy/full-res.
/// Lấy mẫu ngược (inverse-map) song tuyến; ngoài biên = trong suốt.
/// </summary>
public sealed class PerspectiveOp : IResizingOp
{
    public const string Type = "Perspective";
    public string OpType => Type;

    public float Vertical;     // [-1..1] keystone dọc
    public float Horizontal;   // [-1..1] keystone ngang
    public float Rotate;       // độ, xoay phẳng
    public float Scale = 1f;   // phóng để bù viền

    public bool IsIdentity =>
        Near(Vertical) && Near(Horizontal) && Near(Rotate) && MathF.Abs(Scale - 1f) < 1e-4f;
    private static bool Near(float v) => MathF.Abs(v) < 1e-4f;

    public void Apply(LinearImage image, float scale) { /* resizing op */ }

    public LinearImage ApplyResize(LinearImage img, float scale)
    {
        if (IsIdentity) return img;
        int w = img.Width, h = img.Height;
        var dst = new LinearImage(w, h);
        float[] s = img.Pixels, d = dst.Pixels;

        // Dựng homography forward (toạ độ chuẩn hoá tâm gốc [-1..1]) rồi nghịch đảo để inverse-map.
        double[,] fwd = BuildForward(Vertical, Horizontal, Rotate, Scale);
        double[,] inv = Invert3x3(fwd);

        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        float half = MathF.Max(cx, cy);

        Parallel.For(0, h, dy =>
        {
            for (int dx = 0; dx < w; dx++)
            {
                // toạ độ chuẩn hoá tâm gốc.
                double nx = (dx - cx) / half;
                double ny = (dy - cy) / half;
                // áp homography nghịch để tìm điểm nguồn.
                double X = inv[0, 0] * nx + inv[0, 1] * ny + inv[0, 2];
                double Y = inv[1, 0] * nx + inv[1, 1] * ny + inv[1, 2];
                double W = inv[2, 0] * nx + inv[2, 1] * ny + inv[2, 2];
                if (Math.Abs(W) < 1e-9) { ClearPixel(d, (dy * w + dx) * 4); continue; }
                double srcNx = X / W, srcNy = Y / W;
                float sx = (float)(srcNx * half + cx);
                float sy = (float)(srcNy * half + cy);
                Sample(s, w, h, sx, sy, d, (dy * w + dx) * 4);
            }
        });
        return dst;
    }

    // forward map: nguồn(norm) -> đích(norm). dùng để inverse-map đích->nguồn.
    private static double[,] BuildForward(float vert, float horiz, float rotDeg, float scl)
    {
        // Bắt đầu identity.
        double[,] M = Identity();

        // Xoay phẳng.
        if (MathF.Abs(rotDeg) > 1e-4f)
        {
            double a = rotDeg * Math.PI / 180.0;
            double c = Math.Cos(a), s = Math.Sin(a);
            double[,] R = { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } };
            M = Mul(R, M);
        }

        // Keystone: thêm thành phần phối cảnh (hàng thứ 3). Hệ số nhỏ để slider [-1..1] hợp lý.
        const double kp = 0.5;
        double[,] P = Identity();
        P[2, 1] = vert * kp;   // nghiêng dọc -> g (phụ thuộc y)
        P[2, 0] = horiz * kp;  // nghiêng ngang -> h (phụ thuộc x)
        M = Mul(P, M);

        // Phóng để bù viền.
        if (MathF.Abs(scl - 1f) > 1e-5f && scl > 1e-3f)
        {
            double[,] S = { { scl, 0, 0 }, { 0, scl, 0 }, { 0, 0, 1 } };
            M = Mul(S, M);
        }
        return M;
    }

    private static double[,] Identity() => new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    private static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++) sum += a[i, k] * b[k, j];
                r[i, j] = sum;
            }
        return r;
    }

    private static double[,] Invert3x3(double[,] m)
    {
        double a = m[0, 0], b = m[0, 1], c = m[0, 2];
        double d = m[1, 0], e = m[1, 1], f = m[1, 2];
        double g = m[2, 0], h = m[2, 1], i = m[2, 2];
        double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (Math.Abs(det) < 1e-12) return Identity();
        double invDet = 1.0 / det;
        var r = new double[3, 3];
        r[0, 0] = (e * i - f * h) * invDet;
        r[0, 1] = (c * h - b * i) * invDet;
        r[0, 2] = (b * f - c * e) * invDet;
        r[1, 0] = (f * g - d * i) * invDet;
        r[1, 1] = (a * i - c * g) * invDet;
        r[1, 2] = (c * d - a * f) * invDet;
        r[2, 0] = (d * h - e * g) * invDet;
        r[2, 1] = (b * g - a * h) * invDet;
        r[2, 2] = (a * e - b * d) * invDet;
        return r;
    }

    private static void ClearPixel(float[] d, int o)
    {
        d[o] = 0; d[o + 1] = 0; d[o + 2] = 0; d[o + 3] = 0;
    }

    private static void Sample(float[] s, int sw, int sh, float fx, float fy, float[] d, int do_)
    {
        if (fx < 0 || fy < 0 || fx > sw - 1 || fy > sh - 1)
        {
            ClearPixel(d, do_);
            return;
        }
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
            d[do_ + c] = top + (bot - top) * ty;
        }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["vert"] = F(Vertical), ["horiz"] = F(Horizontal), ["rotate"] = F(Rotate), ["scale"] = F(Scale),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static PerspectiveOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Vertical = EditOpRegistry.F(p, "vert"),
        Horizontal = EditOpRegistry.F(p, "horiz"),
        Rotate = EditOpRegistry.F(p, "rotate"),
        Scale = EditOpRegistry.F(p, "scale", 1f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
