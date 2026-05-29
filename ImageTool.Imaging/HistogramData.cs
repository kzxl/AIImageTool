using System;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Phân tích histogram + thống kê clip từ 1 <see cref="LinearImage"/> (11.3). Tính trên giá trị
/// sRGB 8-bit (đúng cái mắt thấy / cái sẽ xuất ra), không phải linear. Thuần dữ liệu nên test được.
/// </summary>
public sealed class HistogramData
{
    public int[] R { get; } = new int[256];
    public int[] G { get; } = new int[256];
    public int[] B { get; } = new int[256];
    public int[] Luma { get; } = new int[256];

    /// <summary>Tổng số pixel đã đếm (mỗi kênh).</summary>
    public long PixelCount { get; private set; }

    /// <summary>% pixel cháy sáng (kênh bất kỳ ở mức 254-255), trên tổng (3 kênh).</summary>
    public double HighlightClipPercent { get; private set; }

    /// <summary>% pixel mất chi tiết tối (kênh bất kỳ ở mức 0-1), trên tổng (3 kênh).</summary>
    public double ShadowClipPercent { get; private set; }

    public bool HighlightClipWarning => HighlightClipPercent > 0.5;
    public bool ShadowClipWarning => ShadowClipPercent > 0.5;

    /// <summary>Tính histogram từ ảnh linear (encode sRGB 8-bit trước khi đếm).</summary>
    public static HistogramData Compute(LinearImage img)
    {
        var h = new HistogramData();
        float[] px = img.Pixels;
        int n = img.PixelCount;
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            byte r = ColorSpace.EncodeByte(px[o]);
            byte g = ColorSpace.EncodeByte(px[o + 1]);
            byte b = ColorSpace.EncodeByte(px[o + 2]);
            h.R[r]++; h.G[g]++; h.B[b]++;
            // luma theo Rec.709 trên sRGB xấp xỉ (đủ cho hiển thị).
            int y = (int)(0.2126f * r + 0.7152f * g + 0.0722f * b + 0.5f);
            if (y > 255) y = 255;
            h.Luma[y]++;
        }
        h.Finish(n);
        return h;
    }

    /// <summary>Tính từ buffer BGRA 8-bit (vd WriteableBitmap preview). stride = bytes/hàng.</summary>
    public static HistogramData ComputeBgra(byte[] bgra, int width, int height, int stride)
    {
        var h = new HistogramData();
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int o = row + x * 4;
                byte bb = bgra[o], gg = bgra[o + 1], rr = bgra[o + 2];
                h.R[rr]++; h.G[gg]++; h.B[bb]++;
                int yy = (int)(0.2126f * rr + 0.7152f * gg + 0.0722f * bb + 0.5f);
                if (yy > 255) yy = 255;
                h.Luma[yy]++;
            }
        }
        h.Finish((long)width * height);
        return h;
    }

    private void Finish(long count)
    {
        PixelCount = count;
        long total = count;
        if (total <= 0) return;
        long hiClip = (long)R[255] + R[254] + G[255] + G[254] + B[255] + B[254];
        long loClip = (long)R[0] + R[1] + G[0] + G[1] + B[0] + B[1];
        HighlightClipPercent = hiClip / (3.0 * total) * 100.0;
        ShadowClipPercent = loClip / (3.0 * total) * 100.0;
    }

    /// <summary>Giá trị max của bất kỳ bin nào (để chuẩn hoá khi vẽ).</summary>
    public int MaxBin()
    {
        int max = 1;
        for (int i = 0; i < 256; i++)
        {
            if (R[i] > max) max = R[i];
            if (G[i] > max) max = G[i];
            if (B[i] > max) max = B[i];
        }
        return max;
    }
}
