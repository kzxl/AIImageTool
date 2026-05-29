using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Ảnh ở dạng float RGBA, premultiplied-free, lưu trong KHÔNG GIAN TUYẾN TÍNH (linear light).
/// Mỗi pixel 4 kênh: R,G,B,A. R/G/B có thể >1.0 (highlight headroom) hoặc &lt;0 trong tính
/// trung gian; chỉ clamp khi encode ra 8/16-bit. A trong [0,1].
///
/// Đây là "đơn vị tiền tệ" của toàn bộ pipeline Develop: mọi op nhận và trả LinearImage.
/// Khác biệt cốt lõi so với xử lý Rgba32 8-bit gamma cũ — cho phép exposure/curve/blur
/// đúng vật lý, không banding, không vỡ vùng sáng.
/// </summary>
public sealed class LinearImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Buffer phẳng, độ dài = Width*Height*4, thứ tự R,G,B,A theo từng pixel.</summary>
    public float[] Pixels { get; }

    public int PixelCount => Width * Height;

    public LinearImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Kích thước ảnh phải > 0.");
        long n = (long)width * height * 4;
        if (n > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "Ảnh quá lớn cho buffer 1 chiều (>2GB float).");
        Width = width;
        Height = height;
        Pixels = new float[n];
    }

    public LinearImage(int width, int height, float[] pixels)
    {
        if (pixels.Length != (long)width * height * 4)
            throw new ArgumentException("Độ dài buffer không khớp width*height*4.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Offset trong Pixels cho pixel (x,y). Không kiểm tra biên (hot path).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Offset(int x, int y) => (y * Width + x) * 4;

    /// <summary>Bản sao sâu (deep clone) — dùng làm "base" bất biến cho edit phi phá hủy.</summary>
    public LinearImage Clone()
    {
        var copy = new float[Pixels.Length];
        Array.Copy(Pixels, copy, Pixels.Length);
        return new LinearImage(Width, Height, copy);
    }

    /// <summary>
    /// Chạy <paramref name="rowAction"/> trên từng hàng y, song song nhiều luồng.
    /// rowAction nhận chỉ số hàng y; tự tính offset bằng Offset(0,y).
    /// Đây là điểm tăng tốc chính: mọi op pixel-wise nên đi qua đây.
    /// </summary>
    public void ProcessRows(Action<int> rowAction)
    {
        Parallel.For(0, Height, rowAction);
    }

    /// <summary>
    /// Chạy op pixel-wise đơn giản: delegate nhận ref 4 kênh (r,g,b,a) của 1 pixel và sửa tại chỗ.
    /// Tự song song theo hàng. Tiện cho op không cần biết toạ độ (exposure, curve, wb...).
    /// </summary>
    public void ProcessPixels(PixelOp op)
    {
        float[] px = Pixels;
        int w = Width;
        Parallel.For(0, Height, y =>
        {
            int baseOff = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int o = baseOff + x * 4;
                op(ref px[o], ref px[o + 1], ref px[o + 2], ref px[o + 3]);
            }
        });
    }

    /// <summary>Đọc 1 pixel (linear) thành tuple. Tiện cho lấy mẫu (white-balance pick, histogram).</summary>
    public (float R, float G, float B, float A) GetPixel(int x, int y)
    {
        int o = Offset(x, y);
        return (Pixels[o], Pixels[o + 1], Pixels[o + 2], Pixels[o + 3]);
    }
}

/// <summary>Delegate sửa tại chỗ 4 kênh linear của 1 pixel.</summary>
public delegate void PixelOp(ref float r, ref float g, ref float b, ref float a);
