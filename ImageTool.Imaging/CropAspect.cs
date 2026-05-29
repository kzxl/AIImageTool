using System;

namespace ImageTool.Imaging;

/// <summary>
/// Tính khung crop chuẩn hoá [0..1] cho 1 tỉ lệ khung hình mục tiêu (13.4). Trả về khung lớn
/// nhất giữ đúng tỉ lệ, căn giữa trong ảnh gốc (kích thước imageW x imageH px). Thuần toán học
/// nên unit test trực tiếp; UI chỉ việc gán kết quả vào CropOp.
/// </summary>
public static class CropAspect
{
    public readonly struct Rect
    {
        public readonly float X, Y, W, H;
        public Rect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
    }

    /// <summary>
    /// Khung crop chuẩn hoá cho tỉ lệ <paramref name="ratioW"/>:<paramref name="ratioH"/> căn giữa
    /// ảnh <paramref name="imageW"/>x<paramref name="imageH"/>. ratio &lt;= 0 -> full khung (1:1 của ảnh).
    /// </summary>
    public static Rect Centered(int imageW, int imageH, double ratioW, double ratioH)
    {
        if (imageW <= 0 || imageH <= 0 || ratioW <= 0 || ratioH <= 0)
            return new Rect(0, 0, 1, 1);

        double targetAspect = ratioW / ratioH;        // w/h mong muốn
        double imageAspect = (double)imageW / imageH;

        double cropWpx, cropHpx;
        if (imageAspect > targetAspect)
        {
            // ảnh rộng hơn tỉ lệ đích -> giới hạn bởi chiều cao.
            cropHpx = imageH;
            cropWpx = cropHpx * targetAspect;
        }
        else
        {
            // ảnh cao hơn -> giới hạn bởi chiều rộng.
            cropWpx = imageW;
            cropHpx = cropWpx / targetAspect;
        }

        float w = (float)(cropWpx / imageW);
        float h = (float)(cropHpx / imageH);
        float x = (1f - w) * 0.5f;
        float y = (1f - h) * 0.5f;
        return new Rect(
            Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f),
            Math.Clamp(w, 0f, 1f), Math.Clamp(h, 0f, 1f));
    }

    /// <summary>Các preset tỉ lệ phổ biến (tên + W:H). Original/Free = (0,0) nghĩa là full.</summary>
    public static readonly (string Name, double W, double H)[] Presets =
    {
        ("Original", 0, 0),
        ("1:1", 1, 1),
        ("4:3", 4, 3),
        ("3:2", 3, 2),
        ("16:9", 16, 9),
        ("5:4", 5, 4),
        ("2:3 (dọc)", 2, 3),
        ("9:16 (dọc)", 9, 16),
    };
}
