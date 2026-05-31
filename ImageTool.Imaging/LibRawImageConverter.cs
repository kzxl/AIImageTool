using System;

namespace ImageTool.Imaging;

/// <summary>
/// Chuyển buffer ảnh đã xử lý của LibRaw (interleaved, 8 hoặc 16 bit/kênh, 3-4 kênh) sang
/// <see cref="LinearImage"/> (RGBA float linear). TÁCH RIÊNG khỏi tầng native để unit test được
/// mà không cần libraw.dll hay file RAW thật.
///
/// QUY ƯỚC: decoder native được cấu hình xuất GAMMA TUYẾN TÍNH + primaries sRGB (xem
/// <see cref="LibRawNative"/>), nên dữ liệu vào đây ĐÃ ở linear-light — chỉ cần chuẩn hoá theo
/// độ sâu bit (chia 255 hoặc 65535), KHÔNG áp lại đường cong sRGB.
/// </summary>
public static class LibRawImageConverter
{
    /// <summary>
    /// Đóng gói buffer LibRaw thành LinearImage. <paramref name="data"/> = pixel interleaved theo hàng;
    /// mỗi pixel <paramref name="colors"/> kênh, mỗi kênh <paramref name="bits"/> bit (8 hoặc 16, 16 =
    /// little-endian 2 byte). Alpha luôn = 1. Kênh thừa (colors==1 grayscale, colors==4) xử lý hợp lý.
    /// </summary>
    public static LinearImage Pack(byte[] data, int width, int height, int colors, int bits)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Kích thước phải > 0.");
        if (colors < 1 || colors > 4) throw new ArgumentOutOfRangeException(nameof(colors), "colors phải 1..4.");
        if (bits != 8 && bits != 16) throw new ArgumentOutOfRangeException(nameof(bits), "bits phải 8 hoặc 16.");

        int bytesPerSample = bits / 8;
        long needed = (long)width * height * colors * bytesPerSample;
        if (data.Length < needed)
            throw new ArgumentException($"Buffer quá nhỏ: cần {needed}, có {data.Length}.", nameof(data));

        var img = new LinearImage(width, height);
        float[] dst = img.Pixels;
        float inv = bits == 16 ? 1f / 65535f : 1f / 255f;
        int pxStrideBytes = colors * bytesPerSample;

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * width * pxStrideBytes;
            int dstRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int s = srcRow + x * pxStrideBytes;
                int d = dstRow + x * 4;
                float r = Sample(data, s, bits) * inv;
                float g = colors >= 2 ? Sample(data, s + bytesPerSample, bits) * inv : r;
                float b = colors >= 3 ? Sample(data, s + 2 * bytesPerSample, bits) * inv : r;
                // colors==1 -> grayscale (g,b = r). colors==4 -> bỏ kênh thứ 4 (thường là G2/alpha).
                dst[d] = r; dst[d + 1] = g; dst[d + 2] = b; dst[d + 3] = 1f;
            }
        }
        return img;
    }

    private static int Sample(byte[] data, int offset, int bits)
        => bits == 16 ? data[offset] | (data[offset + 1] << 8) : data[offset];
}
