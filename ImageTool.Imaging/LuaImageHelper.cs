using System;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Bộ helper cung cấp các hàm xử lý ảnh C# hiệu năng cao (sử dụng Parallel)
/// để đăng ký trực tiếp và cho phép Lua Script gọi, tránh overhead vòng lặp lớn trong Lua.
/// </summary>
public static class LuaImageHelper
{
    /// <summary>
    /// Thay đổi độ sáng và độ tương phản ở mức pixel trong không gian sRGB (tương thích logic cũ)
    /// nhưng thực thi song song tốc độ cực cao trên LinearImage.
    /// </summary>
    public static void AdjustBrightnessContrast(float[] pixels, float brightness, float contrast)
    {
        // Công thức tương thích với thuật toán cũ của ImageEditor
        float contrastFactor = (259.0f * (contrast + 255.0f)) / (255.0f * (259.0f - contrast));

        int pixelCount = pixels.Length / 4;
        Parallel.For(0, pixelCount, i =>
        {
            int idx = i * 4;
            for (int c = 0; c < 3; c++)
            {
                float linVal = pixels[idx + c];
                // 1. Chuyển đổi sang sRGB [0..255]
                float srgbVal = ColorSpace.LinearToSrgb(linVal) * 255f;

                // 2. Áp dụng Brightness & Contrast
                float val = srgbVal + brightness;
                val = contrastFactor * (val - 128.0f) + 128.0f;

                // 3. Clamp giá trị
                if (val < 0f) val = 0f;
                else if (val > 255f) val = 255f;

                // 4. Chuyển đổi ngược lại sang Linear
                pixels[idx + c] = ColorSpace.SrgbToLinear(val / 255f);
            }
        });
    }

    /// <summary>
    /// Lật ngang ảnh trực tiếp trên mảng pixels phẳng.
    /// Trả về mảng pixels mới đã được lật ngang.
    /// </summary>
    public static float[] FlipHorizontal(float[] pixels, int width, int height)
    {
        float[] dst = new float[pixels.Length];
        Parallel.For(0, height, y =>
        {
            int rowOffset = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int srcIdx = rowOffset + x * 4;
                int dstIdx = rowOffset + (width - 1 - x) * 4;

                dst[dstIdx] = pixels[srcIdx];
                dst[dstIdx + 1] = pixels[srcIdx + 1];
                dst[dstIdx + 2] = pixels[srcIdx + 2];
                dst[dstIdx + 3] = pixels[srcIdx + 3];
            }
        });
        return dst;
    }

    /// <summary>
    /// Xoay ảnh 90 độ theo chiều kim đồng hồ (CW) trên mảng pixels phẳng.
    /// Trả về mảng pixels mới (kích thước mới: width = height cũ, height = width cũ).
    /// </summary>
    public static float[] Rotate90CW(float[] pixels, int width, int height)
    {
        float[] dst = new float[pixels.Length];
        int newWidth = height;
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int srcIdx = (y * width + x) * 4;

                // Tọa độ mới sau khi xoay 90CW: nx = height - 1 - y, ny = x
                int nx = newWidth - 1 - y;
                int ny = x;
                int dstIdx = (ny * newWidth + nx) * 4;

                dst[dstIdx] = pixels[srcIdx];
                dst[dstIdx + 1] = pixels[srcIdx + 1];
                dst[dstIdx + 2] = pixels[srcIdx + 2];
                dst[dstIdx + 3] = pixels[srcIdx + 3];
            }
        });
        return dst;
    }
}
