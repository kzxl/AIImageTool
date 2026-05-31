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
    // Cach lam: co dinh h33 = 1, dung he tuyen tinh 8 an. Tao normal equations (A^T A) 8x8 + (A^T b)
    // tu 2N phuong trinh roi giai bang khu Gauss co pivot -> on dinh cho ca minimal (4) lan overdetermined.
    public static double[]? EstimateDlt(IReadOnlyList<Pt> src, IReadOnlyList<Pt> dst)
    {
        int n = Math.Min(src.Count, dst.Count);
        if (n < 4) return null;

        // Chuan hoa diem (Hartley) de on dinh so hoc.
        if (!Normalize(src, n, out double[] T1, out Pt[] s) ||
            !Normalize(dst, n, out double[] T2, out Pt[] d))
            return null;

        // 2N phuong trinh dang: [row] . h(0..7) = rhs, voi h = (h11..h32), h33 = 1.
        //   x*h11 + y*h12 + h13 - u*x*h31 - u*y*h32 = u
        //   x*h21 + y*h22 + h23 - v*x*h31 - v*y*h32 = v
        var ata = new double[8 * 8];
        var atb = new double[8];
        var row = new double[8];
        for (int i = 0; i < n; i++)
        {
            double x = s[i].X, y = s[i].Y, u = d[i].X, v = d[i].Y;

            // Phuong trinh cho u.
            row[0] = x; row[1] = y; row[2] = 1; row[3] = 0; row[4] = 0; row[5] = 0;
            row[6] = -u * x; row[7] = -u * y;
            Accumulate(ata, atb, row, u);

            // Phuong trinh cho v.
            row[0] = 0; row[1] = 0; row[2] = 0; row[3] = x; row[4] = y; row[5] = 1;
            row[6] = -v * x; row[7] = -v * y;
            Accumulate(ata, atb, row, v);
        }

        double[]? hsol = Solve8(ata, atb);
        if (hsol == null) return null;

        double[] hN =
        {
            hsol[0], hsol[1], hsol[2],
            hsol[3], hsol[4], hsol[5],
            hsol[6], hsol[7], 1.0,
        };

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

    // Cong dong 1 phuong trinh (row . h = rhs) vao normal equations A^T A (8x8) + A^T b (8).
    private static void Accumulate(double[] ata, double[] atb, double[] row, double rhs)
    {
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++) ata[r * 8 + c] += row[r] * row[c];
            atb[r] += row[r] * rhs;
        }
    }

    // Giai he 8x8 (A^T A) h = (A^T b) bang khu Gauss-Jordan co pivot. Tra null neu suy bien.
    private static double[]? Solve8(double[] ata, double[] atb)
    {
        const int N = 8;
        var m = new double[N * (N + 1)];
        for (int r = 0; r < N; r++)
        {
            for (int c = 0; c < N; c++) m[r * (N + 1) + c] = ata[r * N + c];
            m[r * (N + 1) + N] = atb[r];
        }
        for (int col = 0; col < N; col++)
        {
            int piv = col; double best = Math.Abs(m[col * (N + 1) + col]);
            for (int r = col + 1; r < N; r++)
            {
                double a = Math.Abs(m[r * (N + 1) + col]);
                if (a > best) { best = a; piv = r; }
            }
            if (best < 1e-14) return null;
            if (piv != col)
                for (int c = 0; c <= N; c++)
                    (m[col * (N + 1) + c], m[piv * (N + 1) + c]) = (m[piv * (N + 1) + c], m[col * (N + 1) + c]);

            double diag = m[col * (N + 1) + col];
            for (int r = 0; r < N; r++)
            {
                if (r == col) continue;
                double f = m[r * (N + 1) + col] / diag;
                if (f == 0) continue;
                for (int c = col; c <= N; c++) m[r * (N + 1) + c] -= f * m[col * (N + 1) + c];
            }
        }
        var x = new double[N];
        for (int r = 0; r < N; r++) x[r] = m[r * (N + 1) + N] / m[r * (N + 1) + r];
        return x;
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
