using System;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Gaussian / Laplacian pyramid (đa tỉ lệ) dùng cho Exposure Fusion + Focus Stacking.
/// Lưu RGB 3 kênh dạng plane float riêng (không dùng alpha). Mỗi tầng có kích thước riêng
/// (giảm ~1/2 mỗi tầng). Reduce = blur 5-tap + downsample; Expand = upsample + blur.
///
/// Một "Pyramid" ở đây lưu LAPLACIAN của 3 kênh RGB (tầng cuối là Gaussian thô nhất),
/// dùng để cộng dồn có trọng số rồi collapse về ảnh. Thuần toán -> test gián tiếp qua fusion.
/// </summary>
public sealed class Pyramid
{
    public int Levels { get; }
    public int[] Widths { get; }
    public int[] Heights { get; }
    // [level][channel] -> plane float (W*H của level).
    public float[][][] Data { get; }

    private Pyramid(int levels, int[] ws, int[] hs)
    {
        Levels = levels;
        Widths = ws;
        Heights = hs;
        Data = new float[levels][][];
        for (int l = 0; l < levels; l++)
        {
            Data[l] = new float[3][];
            for (int c = 0; c < 3; c++) Data[l][c] = new float[ws[l] * hs[l]];
        }
    }

    private static (int[] ws, int[] hs) Dims(int w, int h, int levels)
    {
        var ws = new int[levels]; var hs = new int[levels];
        ws[0] = w; hs[0] = h;
        for (int l = 1; l < levels; l++)
        {
            ws[l] = Math.Max(1, (ws[l - 1] + 1) / 2);
            hs[l] = Math.Max(1, (hs[l - 1] + 1) / 2);
        }
        return (ws, hs);
    }

    /// <summary>Pyramid Gaussian của 1 plane (trọng số). Trả mảng plane theo tầng.</summary>
    public static float[][] BuildGaussian(float[] plane, int w, int h, int levels)
    {
        var (ws, hs) = Dims(w, h, levels);
        var g = new float[levels][];
        g[0] = (float[])plane.Clone();
        for (int l = 1; l < levels; l++)
            g[l] = Reduce(g[l - 1], ws[l - 1], hs[l - 1], ws[l], hs[l]);
        return g;
    }

    /// <summary>Pyramid Laplacian của ảnh RGB (linear). Tầng cuối giữ Gaussian thô.</summary>
    public static Pyramid BuildLaplacianRgb(LinearImage img, int levels)
    {
        int w = img.Width, h = img.Height;
        var (ws, hs) = Dims(w, h, levels);
        var pyr = new Pyramid(levels, ws, hs);

        // tách 3 kênh.
        for (int c = 0; c < 3; c++)
        {
            var gauss = new float[levels][];
            gauss[0] = new float[w * h];
            float[] px = img.Pixels;
            for (int i = 0, p = c; i < w * h; i++, p += 4) gauss[0][i] = px[p];
            for (int l = 1; l < levels; l++)
                gauss[l] = Reduce(gauss[l - 1], ws[l - 1], hs[l - 1], ws[l], hs[l]);

            for (int l = 0; l < levels - 1; l++)
            {
                var up = Expand(gauss[l + 1], ws[l + 1], hs[l + 1], ws[l], hs[l]);
                var lap = pyr.Data[l][c];
                for (int i = 0; i < lap.Length; i++) lap[i] = gauss[l][i] - up[i];
            }
            // tầng cuối = Gaussian thô.
            Array.Copy(gauss[levels - 1], pyr.Data[levels - 1][c], gauss[levels - 1].Length);
        }
        return pyr;
    }

    /// <summary>Tạo pyramid 0 cùng cấu trúc (để cộng dồn).</summary>
    public static Pyramid ZeroLike(Pyramid like) => new(like.Levels, like.Widths, like.Heights);

    /// <summary>this += lap * weightGaussian (weightGaussian là Gaussian pyramid của 1 plane).</summary>
    public void AddWeighted(Pyramid lap, float[][] weightGaussian)
    {
        for (int l = 0; l < Levels; l++)
        {
            int len = Widths[l] * Heights[l];
            var wg = weightGaussian[l];
            for (int c = 0; c < 3; c++)
            {
                var dst = Data[l][c];
                var src = lap.Data[l][c];
                for (int i = 0; i < len; i++) dst[i] += src[i] * wg[i];
            }
        }
    }

    /// <summary>Collapse Laplacian pyramid -> LinearImage (alpha=1).</summary>
    public LinearImage CollapseToImage()
    {
        int levels = Levels;
        var chan = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            // bắt đầu từ tầng thô nhất, cộng dần.
            var cur = (float[])Data[levels - 1][c].Clone();
            int curW = Widths[levels - 1], curH = Heights[levels - 1];
            for (int l = levels - 2; l >= 0; l--)
            {
                var up = Expand(cur, curW, curH, Widths[l], Heights[l]);
                var lap = Data[l][c];
                for (int i = 0; i < up.Length; i++) up[i] += lap[i];
                cur = up; curW = Widths[l]; curH = Heights[l];
            }
            chan[c] = cur;
        }
        int w = Widths[0], h = Heights[0];
        var img = new LinearImage(w, h);
        var px = img.Pixels;
        for (int i = 0, p = 0; i < w * h; i++, p += 4)
        {
            px[p] = chan[0][i]; px[p + 1] = chan[1][i]; px[p + 2] = chan[2][i]; px[p + 3] = 1f;
        }
        return img;
    }

    // --- reduce / expand với kernel Gaussian 5-tap tách trục [1 4 6 4 1]/16 ---
    private static readonly float[] K5 = { 1f / 16f, 4f / 16f, 6f / 16f, 4f / 16f, 1f / 16f };

    private static float[] Reduce(float[] src, int sw, int sh, int dw, int dh)
    {
        var blurred = Blur5(src, sw, sh);
        var dst = new float[dw * dh];
        Parallel.For(0, dh, dy =>
        {
            int sy = Math.Min(sh - 1, dy * 2);
            int drow = dy * dw;
            for (int dx = 0; dx < dw; dx++)
            {
                int sx = Math.Min(sw - 1, dx * 2);
                dst[drow + dx] = blurred[sy * sw + sx];
            }
        });
        return dst;
    }

    private static float[] Expand(float[] src, int sw, int sh, int dw, int dh)
    {
        // upsample (nearest theo /2) rồi blur.
        var up = new float[dw * dh];
        Parallel.For(0, dh, dy =>
        {
            int sy = Math.Min(sh - 1, dy / 2);
            int drow = dy * dw;
            for (int dx = 0; dx < dw; dx++)
            {
                int sx = Math.Min(sw - 1, dx / 2);
                up[drow + dx] = src[sy * sw + sx];
            }
        });
        return Blur5(up, dw, dh);
    }

    private static float[] Blur5(float[] s, int w, int h)
    {
        var tmp = new float[w * h];
        var dst = new float[w * h];
        // ngang
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float acc = 0f;
                for (int k = -2; k <= 2; k++)
                {
                    int xx = x + k; if (xx < 0) xx = 0; else if (xx >= w) xx = w - 1;
                    acc += s[row + xx] * K5[k + 2];
                }
                tmp[row + x] = acc;
            }
        });
        // dọc
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float acc = 0f;
                for (int k = -2; k <= 2; k++)
                {
                    int yy = y + k; if (yy < 0) yy = 0; else if (yy >= h) yy = h - 1;
                    acc += tmp[yy * w + x] * K5[k + 2];
                }
                dst[y * w + x] = acc;
            }
        });
        return dst;
    }
}
