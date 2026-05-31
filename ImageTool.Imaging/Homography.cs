using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

// Homography 3x3 + uoc luong DLT (4+ cap diem) + RANSAC chiu nhieu outlier. Dung cho panorama (#4).
public static class Homography
{
    public readonly struct Pt
    {
        public readonly double X, Y;
        public Pt(double x, double y) { X = x; Y = y; }
    }

    // Ap homography H (row-major 9) cho diem (x,y) -> (x',y') voi chia w.
    public static Pt Apply(double[] h, double x, double y)
    {
        double X = h[0] * x + h[1] * y + h[2];
        double Y = h[3] * x + h[4] * y + h[5];
        double W = h[6] * x + h[7] * y + h[8];
        if (Math.Abs(W) < 1e-12) W = 1e-12;
        return new Pt(X / W, Y / W);
    }

    // Uoc luong H sao cho H*src ~ dst (toi thieu 4 cap). Tra null neu suy bien.
    // Dung DLT: dung he 2N x 9, giai vector null bang power-iteration tren A^T A.
    public static double[]? EstimateDlt(IReadOnlyList<Pt> src, IReadOnlyList<Pt> dst)
    {
        int n = Math.Min(src.Count, dst.Count);
        if (n < 4) return null;

        // Chuan hoa diem (Hartley) de on dinh so hoc.
        if (!Normalize(src, n, out double[] T1, out Pt[] s) ||
            !Normalize(dst, n, out double[] T2, out Pt[] d))
            return null;

        // A (2n x 9).
        var A = new double[2 * n][];
        for (int i = 0; i < n; i++)
        {
            double x = s[i].X, y = s[i].Y, u = d[i].X, v = d[i].Y;
            A[2 * i] = new double[] { -x, -y, -1, 0, 0, 0, u * x, u * y, u };
            A[2 * i + 1] = new double[] { 0, 0, 0, -x, -y, -1, v * x, v * y, v };
        }

        // A^T A (9x9).
        var ata = new double[9 * 9];
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
            {
                double sum = 0;
                for (int k = 0; k < 2 * n; k++) sum += A[k][r] * A[k][c];
                ata[r * 9 + c] = sum;
            }

        // Vector rieng ung voi tri rieng nho nhat = null-space. Inverse power iteration tren (A^T A).
        double[]? hN = SmallestEigenVector(ata);
        if (hN == null) return null;

        // Giai chuan hoa: H = T2^-1 * Hn * T1.
        double[] t2inv = Invert3x3(T2);
        double[] H = Mul3x3(Mul3x3(t2inv, hN), T1);

        // Chuan hoa H[8] = 1.
        if (Math.Abs(H[8]) > 1e-12)
            for (int i = 0; i < 9; i++) H[i] /= H[8];
        return H;
    }

    // RANSAC: chon ngau nhien 4 cap, fit DLT, dem inlier theo nguong reproj (px). Tra H tot nhat + mask inlier.
    public static double[]? EstimateRansac(IReadOnlyList<Pt> src, IReadOnlyList<Pt> dst,
        out bool[] inliers, double threshold = 3.0, int iterations = 500, int seed = 12345)
    {
        int n = Math.Min(src.Count, dst.Count);
        inliers = new bool[n];
        if (n < 4) return null;

        var rng = new Random(seed);
        double[]? bestH = null;
        int bestCount = -1;
        double thr2 = threshold * threshold;
        var pick = new int[4];

        for (int it = 0; it < iterations; it++)
        {
            // Chon 4 chi so phan biet.
            if (!Pick4(rng, n, pick)) continue;
            var s4 = new Pt[4]; var d4 = new Pt[4];
            for (int i = 0; i < 4; i++) { s4[i] = src[pick[i]]; d4[i] = dst[pick[i]]; }
            var H = EstimateDlt(s4, d4);
            if (H == null) continue;

            int count = 0;
            for (int i = 0; i < n; i++)
            {
                var p = Apply(H, src[i].X, src[i].Y);
                double dx = p.X - dst[i].X, dy = p.Y - dst[i].Y;
                if (dx * dx + dy * dy <= thr2) count++;
            }
            if (count > bestCount) { bestCount = count; bestH = H; }
        }

        if (bestH == null) return null;

        // Lay tap inlier theo bestH roi re-fit DLT tren toan bo inlier (chinh xac hon).
        var inSrc = new List<Pt>(); var inDst = new List<Pt>();
        for (int i = 0; i < n; i++)
        {
            var p = Apply(bestH, src[i].X, src[i].Y);
            double dx = p.X - dst[i].X, dy = p.Y - dst[i].Y;
            bool ok = dx * dx + dy * dy <= thr2;
            inliers[i] = ok;
            if (ok) { inSrc.Add(src[i]); inDst.Add(dst[i]); }
        }
        if (inSrc.Count >= 4)
        {
            var refined = EstimateDlt(inSrc, inDst);
            if (refined != null) return refined;
        }
        return bestH;
    }

    private static bool Pick4(Random rng, int n, int[] outIdx)
    {
        if (n < 4) return false;
        for (int i = 0; i < 4; i++)
        {
            int tries = 0;
            while (true)
            {
                int c = rng.Next(n);
                bool dup = false;
                for (int j = 0; j < i; j++) if (outIdx[j] == c) { dup = true; break; }
                if (!dup) { outIdx[i] = c; break; }
                if (++tries > 50) return false;
            }
        }
        return true;
    }

    // Chuan hoa Hartley: dich ve centroid, scale sao cho khoang cach trung binh = sqrt(2). Tra ma tran T.
    private static bool Normalize(IReadOnlyList<Pt> pts, int n, out double[] T, out Pt[] outPts)
    {
        T = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        outPts = new Pt[n];
        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++) { cx += pts[i].X; cy += pts[i].Y; }
        cx /= n; cy /= n;
        double mean = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = pts[i].X - cx, dy = pts[i].Y - cy;
            mean += Math.Sqrt(dx * dx + dy * dy);
        }
        mean /= n;
        if (mean < 1e-12) return false;
        double scale = Math.Sqrt(2) / mean;
        T = new double[] { scale, 0, -scale * cx, 0, scale, -scale * cy, 0, 0, 1 };
        for (int i = 0; i < n; i++)
            outPts[i] = new Pt((pts[i].X - cx) * scale, (pts[i].Y - cy) * scale);
        return true;
    }

    // Eigenvector ung voi tri rieng NHO NHAT cua ma tran doi xung 9x9 (semi-pos-def A^T A):
    // dung shift: B = mu*I - M (mu = trace), power-iter tren B -> eigenvector lon nhat cua B = nho nhat cua M.
    private static double[]? SmallestEigenVector(double[] m)
    {
        const int N = 9;
        double trace = 0; for (int i = 0; i < N; i++) trace += m[i * N + i];
        double mu = trace + 1.0;
        var B = new double[N * N];
        for (int i = 0; i < N * N; i++) B[i] = -m[i];
        for (int i = 0; i < N; i++) B[i * N + i] += mu;

        var v = new double[N];
        var rngLocal = new Random(7);
        for (int i = 0; i < N; i++) v[i] = rngLocal.NextDouble() - 0.5;
        Normalize9(v);

        var next = new double[N];
        for (int iter = 0; iter < 300; iter++)
        {
            for (int r = 0; r < N; r++)
            {
                double sum = 0;
                for (int c = 0; c < N; c++) sum += B[r * N + c] * v[c];
                next[r] = sum;
            }
            if (!Normalize9(next)) return null;
            double diff = 0;
            for (int i = 0; i < N; i++) diff += Math.Abs(next[i] - v[i]);
            Array.Copy(next, v, N);
            if (diff < 1e-12) break;
        }
        return (double[])v.Clone();
    }

    private static bool Normalize9(double[] v)
    {
        double norm = 0; for (int i = 0; i < v.Length; i++) norm += v[i] * v[i];
        norm = Math.Sqrt(norm);
        if (norm < 1e-15) return false;
        for (int i = 0; i < v.Length; i++) v[i] /= norm;
        return true;
    }

    public static double[] Mul3x3(double[] a, double[] b)
    {
        var r = new double[9];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                r[row * 3 + col] =
                    a[row * 3 + 0] * b[0 * 3 + col] +
                    a[row * 3 + 1] * b[1 * 3 + col] +
                    a[row * 3 + 2] * b[2 * 3 + col];
        return r;
    }

    public static double[] Invert3x3(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
        double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
        double det = a * A + b * B + c * C;
        if (Math.Abs(det) < 1e-15) return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double inv = 1.0 / det;
        return new double[]
        {
            A * inv, -(b * i - c * h) * inv, (b * f - c * e) * inv,
            B * inv, (a * i - c * g) * inv, -(a * f - c * d) * inv,
            C * inv, -(a * h - b * g) * inv, (a * e - b * d) * inv,
        };
    }
}
