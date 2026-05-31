using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

/// <summary>
/// Perceptual hash (#1) — vân tay 64-bit của ảnh để phát hiện ảnh TRÙNG / GẦN TRÙNG (burst, ảnh lặp,
/// cùng cảnh khác phơi sáng nhẹ). Hỗ trợ:
///   - dHash (difference hash): so sánh độ sáng pixel kề nhau trên lưới 9x8 -> 64 bit. Bền với resize/nén.
///   - aHash (average hash): bit = pixel sáng hơn trung bình lưới 8x8. Đơn giản, bổ trợ.
/// Khoảng cách Hamming giữa 2 hash -> độ giống. Thuần toán học -> unit test trực tiếp.
/// </summary>
public static class PerceptualHash
{
    /// <summary>dHash 64-bit từ LinearImage. Thu nhỏ về 9x8 luminance rồi so sánh ngang.</summary>
    public static ulong DHash(LinearImage img)
    {
        const int w = 9, h = 8;
        float[] g = SampleGray(img, w, h);
        ulong hash = 0; int bit = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w - 1; x++)
            {
                if (g[y * w + x] < g[y * w + x + 1]) hash |= 1UL << bit;
                bit++;
            }
        return hash;
    }

    /// <summary>aHash 64-bit: lưới 8x8, bit = sáng hơn trung bình.</summary>
    public static ulong AHash(LinearImage img)
    {
        const int n = 8;
        float[] g = SampleGray(img, n, n);
        double mean = 0;
        for (int i = 0; i < g.Length; i++) mean += g[i];
        mean /= g.Length;
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (g[i] >= mean) hash |= 1UL << i;
        return hash;
    }

    /// <summary>Khoảng cách Hamming (số bit khác) giữa 2 hash — 0 = giống hệt, 64 = ngược hoàn toàn.</summary>
    public static int Distance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);

    /// <summary>Độ giống 0..1 (1 = giống hệt) từ khoảng cách Hamming dHash 64-bit.</summary>
    public static float Similarity(ulong a, ulong b) => 1f - Distance(a, b) / 64f;

    /// <summary>
    /// Gom các ảnh có dHash gần nhau thành nhóm (near-duplicate). <paramref name="threshold"/> =
    /// khoảng cách Hamming tối đa coi là "gần trùng" (mặc định 10 ~ giống ~84%). Trả các nhóm có &gt;1 phần tử.
    /// Dùng union-find đơn giản trên cặp gần nhau.
    /// </summary>
    public static List<List<int>> GroupSimilar(IReadOnlyList<ulong> hashes, int threshold = 10)
    {
        int n = hashes.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Distance(hashes[i], hashes[j]) <= threshold) Union(i, j);

        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!groups.TryGetValue(root, out var list)) { list = new List<int>(); groups[root] = list; }
            list.Add(i);
        }

        var result = new List<List<int>>();
        foreach (var g in groups.Values)
            if (g.Count > 1) result.Add(g);
        return result;
    }

    /// <summary>Lưới luminance (sRGB-encoded 0..1) kích thước w x h bằng box-sample. Cho hash ổn định.</summary>
    private static float[] SampleGray(LinearImage img, int w, int h)
    {
        var g = new float[w * h];
        int iw = img.Width, ih = img.Height;
        var px = img.Pixels;
        for (int gy = 0; gy < h; gy++)
        {
            int sy0 = (int)((long)gy * ih / h);
            int sy1 = Math.Max(sy0 + 1, (int)((long)(gy + 1) * ih / h));
            for (int gx = 0; gx < w; gx++)
            {
                int sx0 = (int)((long)gx * iw / w);
                int sx1 = Math.Max(sx0 + 1, (int)((long)(gx + 1) * iw / w));
                double sum = 0; int cnt = 0;
                for (int y = sy0; y < sy1 && y < ih; y++)
                    for (int x = sx0; x < sx1 && x < iw; x++)
                    {
                        int o = (y * iw + x) * 4;
                        float lum = 0.2126f * px[o] + 0.7152f * px[o + 1] + 0.0722f * px[o + 2];
                        sum += ColorSpace.LinearToSrgb(lum);
                        cnt++;
                    }
                g[gy * w + gx] = cnt > 0 ? (float)(sum / cnt) : 0f;
            }
        }
        return g;
    }
}
