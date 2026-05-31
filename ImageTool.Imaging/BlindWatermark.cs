using System;
using System.Text;

namespace ImageTool.Imaging;

// Watermark vo hinh (blind watermark) kieu DCT + QIM (quantization index modulation).
// Nhung 1 chuoi bit vao he so DCT tan trung cua cac khoi 8x8 tren kenh luminance (sRGB domain),
// lap lai nhieu lan (redundancy) roi giai ma bang bo phieu da so -> ben voi nen JPEG nhe + resize nho.
// Khong can native; thuan toan hoc -> unit test truc tiep (embed -> extract round-trip).
public static class BlindWatermark
{
    private const int N = 8;                 // kich thuoc khoi DCT
    // 2 he so tan trung de nhung (tranh DC va tan cao de ben hon).
    private const int Cu1 = 3, Cv1 = 1;
    private const int Cu2 = 1, Cv2 = 3;
    private const float Step = 12f;          // buoc luong tu QIM (tren thang 0..255). Lon = ben hon nhung lo hon.

    // Header dong bo + do dai (16 bit) de extract biet bat dau & so byte.
    private const uint Magic = 0xA5;         // 8 bit magic

    // === DCT 8x8 (tach hang/cot, du nhanh cho watermark) ===
    private static readonly float[,] CosLut = BuildCosLut();
    private static readonly float[] AlphaLut = BuildAlphaLut();

    private static float[,] BuildCosLut()
    {
        var c = new float[N, N];
        for (int u = 0; u < N; u++)
            for (int x = 0; x < N; x++)
                c[u, x] = MathF.Cos((2 * x + 1) * u * MathF.PI / (2 * N));
        return c;
    }
    private static float[] BuildAlphaLut()
    {
        var a = new float[N];
        a[0] = MathF.Sqrt(1f / N);
        for (int u = 1; u < N; u++) a[u] = MathF.Sqrt(2f / N);
        return a;
    }

    private static void Dct2(float[,] block, float[,] outc)
    {
        var tmp = new float[N, N];
        // theo hang
        for (int y = 0; y < N; y++)
            for (int u = 0; u < N; u++)
            {
                float s = 0f;
                for (int x = 0; x < N; x++) s += block[y, x] * CosLut[u, x];
                tmp[y, u] = AlphaLut[u] * s;
            }
        // theo cot
        for (int u = 0; u < N; u++)
            for (int v = 0; v < N; v++)
            {
                float s = 0f;
                for (int y = 0; y < N; y++) s += tmp[y, u] * CosLut[v, y];
                outc[v, u] = AlphaLut[v] * s;
            }
    }

    private static void Idct2(float[,] coef, float[,] outb)
    {
        var tmp = new float[N, N];
        for (int u = 0; u < N; u++)
            for (int y = 0; y < N; y++)
            {
                float s = 0f;
                for (int v = 0; v < N; v++) s += AlphaLut[v] * coef[v, u] * CosLut[v, y];
                tmp[y, u] = s;
            }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float s = 0f;
                for (int u = 0; u < N; u++) s += AlphaLut[u] * tmp[y, u] * CosLut[u, x];
                outb[y, x] = s;
            }
    }

    // === Chuyen message -> chuoi bit (magic 8b + length 16b + payload) ===
    private static bool[] MessageToBits(string message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message ?? "");
        int len = Math.Min(payload.Length, 0xFFFF);
        var bits = new bool[8 + 16 + len * 8];
        int bi = 0;
        WriteByteBits(bits, ref bi, (byte)Magic);
        WriteByteBits(bits, ref bi, (byte)(len >> 8));
        WriteByteBits(bits, ref bi, (byte)(len & 0xFF));
        for (int i = 0; i < len; i++) WriteByteBits(bits, ref bi, payload[i]);
        return bits;
    }

    private static void WriteByteBits(bool[] bits, ref int bi, byte b)
    {
        for (int k = 7; k >= 0; k--) bits[bi++] = ((b >> k) & 1) == 1;
    }

    // === QIM nhung/giai 1 bit vao 1 he so ===
    private static float QimEmbed(float coef, bool bit)
    {
        // luong tu ve boi cua Step, dich nua buoc theo bit.
        float q = MathF.Round(coef / Step) * Step;
        float offset = bit ? Step * 0.25f : -Step * 0.25f;
        return q + offset;
    }
    private static bool QimExtract(float coef)
    {
        float r = coef - MathF.Round(coef / Step) * Step; // phan du quanh boi gan nhat
        return r >= 0f;
    }

    // === API cong khai ===

    // Nhung watermark text vao anh (sua tai cho tren ban sao). Lap chuoi bit qua toan bo cac khoi 8x8
    // (2 he so/khoi) de tang ben. Tra ve so khoi da dung; <=0 neu anh qua nho.
    public static int Embed(LinearImage image, string message)
    {
        if (image == null) return 0;
        bool[] bits = MessageToBits(message);
        if (bits.Length == 0) return 0;

        int w = image.Width, h = image.Height;
        int bx = w / N, by = h / N;
        if (bx <= 0 || by <= 0) return 0;

        // Lay luminance sRGB (0..255) ra mang lam viec.
        float[] lum = ExtractLumByte(image);

        int bitIdx = 0, blocks = 0;
        var blk = new float[N, N];
        var coef = new float[N, N];
        for (int byi = 0; byi < by; byi++)
            for (int bxi = 0; bxi < bx; bxi++)
            {
                LoadBlock(lum, w, bxi * N, byi * N, blk);
                Dct2(blk, coef);
                bool b1 = bits[bitIdx % bits.Length];
                bool b2 = bits[(bitIdx + 1) % bits.Length];
                coef[Cv1, Cu1] = QimEmbed(coef[Cv1, Cu1], b1);
                coef[Cv2, Cu2] = QimEmbed(coef[Cv2, Cu2], b2);
                Idct2(coef, blk);
                StoreBlock(lum, w, bxi * N, byi * N, blk);
                bitIdx += 2;
                blocks++;
            }

        ApplyLumByte(image, lum);
        return blocks;
    }

    // Giai watermark. Tra null neu khong tim thay header magic hop le. Dung bo phieu da so tren
    // cac lan lap de chong nhieu (JPEG/resize). maxBytesGuess gioi han do dai payload doc thu.
    public static string? Extract(LinearImage image)
    {
        if (image == null) return null;
        int w = image.Width, h = image.Height;
        int bx = w / N, by = h / N;
        if (bx <= 0 || by <= 0) return null;

        float[] lum = ExtractLumByte(image);

        // Doc tat ca bit theo thu tu khoi (2 bit/khoi).
        int total = bx * by * 2;
        var raw = new bool[total];
        int ri = 0;
        var blk = new float[N, N];
        var coef = new float[N, N];
        for (int byi = 0; byi < by; byi++)
            for (int bxi = 0; bxi < bx; bxi++)
            {
                LoadBlock(lum, w, bxi * N, byi * N, blk);
                Dct2(blk, coef);
                raw[ri++] = QimExtract(coef[Cv1, Cu1]);
                raw[ri++] = QimExtract(coef[Cv2, Cu2]);
            }

        return DecodeWithVoting(raw);
    }

    // Thu cac chu ky lap (period) hop ly: vi embed lap chuoi bit, ta khong biet do dai message truoc.
    // Cach: gia dinh period = 24 + len*8; thu tang dan len, voting tung vi tri, kiem magic + length khop.
    private static string? DecodeWithVoting(bool[] raw)
    {
        // Thu cac do dai payload tu 0..256 byte.
        for (int len = 0; len <= 256; len++)
        {
            int period = 24 + len * 8;
            if (period > raw.Length) break;
            var voted = VoteBits(raw, period);
            int bi = 0;
            byte magic = ReadByte(voted, ref bi);
            if (magic != (byte)Magic) continue;
            int hi = ReadByte(voted, ref bi);
            int lo = ReadByte(voted, ref bi);
            int gotLen = (hi << 8) | lo;
            if (gotLen != len) continue;
            var bytes = new byte[len];
            for (int i = 0; i < len; i++) bytes[i] = ReadByte(voted, ref bi);
            try { return Encoding.UTF8.GetString(bytes); }
            catch { return null; }
        }
        return null;
    }

    // Bo phieu da so cho moi vi tri bit trong 1 chu ky.
    private static bool[] VoteBits(bool[] raw, int period)
    {
        var ones = new int[period];
        var cnt = new int[period];
        for (int i = 0; i < raw.Length; i++)
        {
            int p = i % period;
            cnt[p]++;
            if (raw[i]) ones[p]++;
        }
        var res = new bool[period];
        for (int p = 0; p < period; p++) res[p] = ones[p] * 2 >= cnt[p];
        return res;
    }

    private static byte ReadByte(bool[] bits, ref int bi)
    {
        int b = 0;
        for (int k = 0; k < 8; k++) { b = (b << 1) | (bits[bi++] ? 1 : 0); }
        return (byte)b;
    }

    // === Luminance sRGB byte helpers ===
    private static float[] ExtractLumByte(LinearImage img)
    {
        int n = img.Width * img.Height;
        var lum = new float[n];
        var px = img.Pixels;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            float r = ColorSpace.LinearToSrgb(px[o]);
            float g = ColorSpace.LinearToSrgb(px[o + 1]);
            float b = ColorSpace.LinearToSrgb(px[o + 2]);
            lum[i] = (0.299f * r + 0.587f * g + 0.114f * b) * 255f;
        }
        return lum;
    }

    // Ap delta luminance (sRGB) tro lai anh linear, giu hue: cong cung 1 delta sRGB cho R/G/B.
    private static void ApplyLumByte(LinearImage img, float[] newLum)
    {
        int n = img.Width * img.Height;
        var px = img.Pixels;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            float r = ColorSpace.LinearToSrgb(px[o]);
            float g = ColorSpace.LinearToSrgb(px[o + 1]);
            float b = ColorSpace.LinearToSrgb(px[o + 2]);
            float oldLum = (0.299f * r + 0.587f * g + 0.114f * b) * 255f;
            float delta = (newLum[i] - oldLum) / 255f;
            px[o] = ColorSpace.SrgbToLinear(Clamp01(r + delta));
            px[o + 1] = ColorSpace.SrgbToLinear(Clamp01(g + delta));
            px[o + 2] = ColorSpace.SrgbToLinear(Clamp01(b + delta));
        }
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private static void LoadBlock(float[] lum, int w, int x0, int y0, float[,] blk)
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                blk[y, x] = lum[(y0 + y) * w + (x0 + x)];
    }
    private static void StoreBlock(float[] lum, int w, int x0, int y0, float[,] blk)
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                lum[(y0 + y) * w + (x0 + x)] = blk[y, x];
    }
}
