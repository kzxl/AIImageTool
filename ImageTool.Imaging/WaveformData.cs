using System;

namespace ImageTool.Imaging;

/// <summary>
/// Waveform / RGB-Parade scope: phân bố cường độ theo TỪNG CỘT ảnh (khác histogram gộp toàn ảnh).
/// Mỗi cột x của ảnh được gộp về <see cref="Columns"/> cột scope; với mỗi cột đếm số pixel rơi vào
/// 256 mức cường độ (0 = đáy/đen, 255 = đỉnh/trắng). Dùng để soi clip theo vị trí ngang + cân bằng màu.
///
/// Tính trên sRGB 8-bit (cái mắt thấy). Thuần dữ liệu nên test trực tiếp, không phụ thuộc UI.
/// </summary>
public sealed class WaveformData
{
    public int Columns { get; }

    /// <summary>Mật độ luma: [cột, mức 0..255]. Giá trị = số pixel cột đó ở mức đó.</summary>
    public int[,] Luma { get; }
    public int[,] R { get; }
    public int[,] G { get; }
    public int[,] B { get; }

    /// <summary>Giá trị đếm lớn nhất (để chuẩn hoá độ sáng khi vẽ).</summary>
    public int MaxCount { get; private set; }

    private WaveformData(int columns)
    {
        Columns = columns;
        Luma = new int[columns, 256];
        R = new int[columns, 256];
        G = new int[columns, 256];
        B = new int[columns, 256];
    }

    /// <summary>
    /// Tính waveform từ ảnh linear. <paramref name="columns"/> = số cột scope (gộp ngang). Mặc định 256.
    /// </summary>
    public static WaveformData Compute(LinearImage img, int columns = 256)
    {
        if (columns < 1) columns = 1;
        int w = img.Width, h = img.Height;
        if (columns > w) columns = w;
        var wf = new WaveformData(columns);
        float[] px = img.Pixels;

        for (int y = 0; y < h; y++)
        {
            int rowOff = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int o = rowOff + x * 4;
                byte r = ColorSpace.EncodeByte(px[o]);
                byte g = ColorSpace.EncodeByte(px[o + 1]);
                byte b = ColorSpace.EncodeByte(px[o + 2]);
                int col = (int)((long)x * columns / w);
                if (col >= columns) col = columns - 1;
                int luma = (int)(0.2126f * r + 0.7152f * g + 0.0722f * b + 0.5f);
                if (luma > 255) luma = 255;
                wf.Luma[col, luma]++;
                wf.R[col, r]++;
                wf.G[col, g]++;
                wf.B[col, b]++;
            }
        }

        // MaxCount để chuẩn hoá (lấy max trên luma — đại diện mật độ chung).
        int max = 1;
        for (int c = 0; c < columns; c++)
            for (int v = 0; v < 256; v++)
                if (wf.Luma[c, v] > max) max = wf.Luma[c, v];
        wf.MaxCount = max;
        return wf;
    }
}
