using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Xoay 90°/180°/270° + lật ngang/dọc. Là IResizingOp vì xoay 90/270 đổi W&lt;->H.
/// </summary>
public sealed class OrientationOp : IResizingOp
{
    public const string Type = "Orientation";
    public string OpType => Type;
    public int Rotate90;       // 0,1,2,3 (số lần xoay 90° CW)
    public bool FlipH, FlipV;

    public bool IsIdentity => (Rotate90 % 4) == 0 && !FlipH && !FlipV;

    public void Apply(LinearImage image, float scale) { /* resizing op: dùng ApplyResize */ }

    public LinearImage ApplyResize(LinearImage img, float scale)
    {
        if (IsIdentity) return img;
        var cur = img;
        int rot = ((Rotate90 % 4) + 4) % 4;
        for (int i = 0; i < rot; i++) cur = Rotate90CW(cur);
        if (FlipH) cur = Flip(cur, true);
        if (FlipV) cur = Flip(cur, false);
        return cur;
    }

    private static LinearImage Rotate90CW(LinearImage src)
    {
        int w = src.Width, h = src.Height;
        var dst = new LinearImage(h, w); // W<->H
        float[] s = src.Pixels, d = dst.Pixels;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int so = (y * w + x) * 4;
                // (x,y) -> (h-1-y, x) trong ảnh mới rộng h.
                int nx = h - 1 - y, ny = x;
                int do_ = (ny * h + nx) * 4;
                d[do_] = s[so]; d[do_ + 1] = s[so + 1]; d[do_ + 2] = s[so + 2]; d[do_ + 3] = s[so + 3];
            }
        });
        return dst;
    }

    private static LinearImage Flip(LinearImage src, bool horizontal)
    {
        int w = src.Width, h = src.Height;
        var dst = new LinearImage(w, h);
        float[] s = src.Pixels, d = dst.Pixels;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int nx = horizontal ? w - 1 - x : x;
                int ny = horizontal ? y : h - 1 - y;
                int so = (y * w + x) * 4;
                int do_ = (ny * w + nx) * 4;
                d[do_] = s[so]; d[do_ + 1] = s[so + 1]; d[do_ + 2] = s[so + 2]; d[do_ + 3] = s[so + 3];
            }
        });
        return dst;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["rot"] = Rotate90.ToString(CultureInfo.InvariantCulture),
        ["flipH"] = FlipH ? "true" : "false",
        ["flipV"] = FlipV ? "true" : "false",
    };
    public static OrientationOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Rotate90 = EditOpRegistry.I(p, "rot"),
        FlipH = EditOpRegistry.B(p, "flipH"),
        FlipV = EditOpRegistry.B(p, "flipV"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Áp EXIF Orientation (giá trị 1..8 theo chuẩn TIFF/EXIF) vào pixel để ảnh hiển thị đúng chiều.
/// Máy ảnh thường lưu ảnh theo chiều sensor + cờ orientation; nếu không "bake" cờ này, ảnh chụp
/// dọc sẽ hiện nằm ngang. Map sang <see cref="OrientationOp"/> (rotate 90 + flip) rồi áp 1 lần lúc decode.
///
/// Bảng EXIF: 1=normal, 2=flipH, 3=rot180, 4=flipV, 5=transpose, 6=rot90CW, 7=transverse, 8=rot270CW.
/// </summary>
public static class ExifOrientation
{
    /// <summary>Trả OrientationOp tương ứng giá trị EXIF (identity nếu 1 hoặc ngoài 1..8).</summary>
    public static OrientationOp ToOp(int exif) => exif switch
    {
        2 => new OrientationOp { FlipH = true },
        3 => new OrientationOp { Rotate90 = 2 },
        4 => new OrientationOp { FlipV = true },
        // 5 (transpose) = lật theo đường chéo chính = rot90CW + flipH.
        5 => new OrientationOp { Rotate90 = 1, FlipH = true },
        6 => new OrientationOp { Rotate90 = 1 },
        // 7 (transverse) = rot90CW + flipV.
        7 => new OrientationOp { Rotate90 = 1, FlipV = true },
        8 => new OrientationOp { Rotate90 = 3 },
        _ => new OrientationOp(),
    };

    /// <summary>
    /// Bake orientation vào ảnh linear: trả ảnh đã xoay/lật đúng chiều (ảnh mới nếu cần, hoặc chính nó
    /// nếu orientation = normal). Dùng ngay sau decode để pixel luôn đúng chiều xem.
    /// </summary>
    public static LinearImage Bake(LinearImage img, int exif)
    {
        var op = ToOp(exif);
        return op.IsIdentity ? img : op.ApplyResize(img, 1f);
    }
}

/// <summary>
/// Crop + Straighten. Cắt theo hình chữ nhật chuẩn hoá [0..1] (X,Y,W,H) và xoay tự do
/// (Angle độ, quanh tâm crop) bằng nội suy song tuyến. Toạ độ chuẩn hoá nên khớp proxy/full-res.
/// </summary>
public sealed class CropOp : IResizingOp
{
    public const string Type = "Crop";
    public string OpType => Type;
    public float X, Y, W = 1f, H = 1f; // chuẩn hoá
    public float Angle;                 // độ, xoay thẳng (straighten), -45..45

    public bool IsIdentity =>
        Near(X, 0) && Near(Y, 0) && Near(W, 1) && Near(H, 1) && Near(Angle, 0);
    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-4f;

    public void Apply(LinearImage image, float scale) { /* resizing op */ }

    public LinearImage ApplyResize(LinearImage img, float scale)
    {
        if (IsIdentity) return img;
        int sw = img.Width, sh = img.Height;
        float cropX = X * sw, cropY = Y * sh, cropW = W * sw, cropH = H * sh;
        int dw = Math.Max(1, (int)MathF.Round(cropW));
        int dh = Math.Max(1, (int)MathF.Round(cropH));
        var dst = new LinearImage(dw, dh);
        float[] s = img.Pixels, d = dst.Pixels;

        float ang = Angle * MathF.PI / 180f;
        float cos = MathF.Cos(ang), sin = MathF.Sin(ang);
        // tâm crop trong ảnh nguồn.
        float ccx = cropX + cropW * 0.5f, ccy = cropY + cropH * 0.5f;

        Parallel.For(0, dh, dy =>
        {
            for (int dx = 0; dx < dw; dx++)
            {
                // toạ độ trong crop (gốc giữa).
                float rx = dx - dw * 0.5f;
                float ry = dy - dh * 0.5f;
                // xoay rồi dịch về tâm crop nguồn.
                float sx = ccx + (rx * cos - ry * sin);
                float sy = ccy + (rx * sin + ry * cos);
                int do_ = (dy * dw + dx) * 4;
                Sample(s, sw, sh, sx, sy, d, do_);
            }
        });
        return dst;
    }

    private static void Sample(float[] s, int sw, int sh, float fx, float fy, float[] d, int do_)
    {
        if (fx < 0 || fy < 0 || fx > sw - 1 || fy > sh - 1)
        {
            d[do_] = 0; d[do_ + 1] = 0; d[do_ + 2] = 0; d[do_ + 3] = 0; // ngoài biên = trong suốt
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
        ["x"] = F(X), ["y"] = F(Y), ["w"] = F(W), ["h"] = F(H), ["angle"] = F(Angle),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static CropOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        X = EditOpRegistry.F(p, "x"), Y = EditOpRegistry.F(p, "y"),
        W = EditOpRegistry.F(p, "w", 1f), H = EditOpRegistry.F(p, "h", 1f),
        Angle = EditOpRegistry.F(p, "angle"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
