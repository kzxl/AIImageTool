using System;
using System.Runtime.CompilerServices;

namespace ImageTool.Imaging;

/// <summary>
/// Chuyển đổi giữa sRGB (gamma-encoded, cái mà file PNG/JPG lưu) và linear light
/// (cái mà mọi phép tính toán quang học phải chạy trên đó).
///
/// Pipeline: decode byte sRGB -> linear float (qua bảng LUT 256 phần tử, nhanh),
/// xử lý toàn bộ ở linear, rồi encode linear -> sRGB byte/ushort khi xuất.
/// </summary>
public static class ColorSpace
{
    // LUT 8-bit sRGB -> linear. Dựng 1 lần, tra cứu O(1) khi decode.
    private static readonly float[] SrgbToLinear8 = BuildSrgbToLinear8();

    private static float[] BuildSrgbToLinear8()
    {
        var lut = new float[256];
        for (int i = 0; i < 256; i++)
            lut[i] = SrgbToLinear(i / 255f);
        return lut;
    }

    /// <summary>Tra nhanh 1 kênh byte sRGB (0..255) sang linear float.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DecodeByte(byte v) => SrgbToLinear8[v];

    /// <summary>sRGB component [0..1] -> linear [0..1]. Công thức IEC 61966-2-1 chuẩn.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SrgbToLinear(float c)
    {
        if (c <= 0.04045f) return c / 12.92f;
        return MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    /// <summary>linear [0..1] -> sRGB component [0..1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LinearToSrgb(float c)
    {
        if (c <= 0f) return 0f;
        if (c >= 1f) return 1f;
        if (c <= 0.0031308f) return c * 12.92f;
        return 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    /// <summary>Encode 1 kênh linear -> byte sRGB, có clamp.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte EncodeByte(float linear)
    {
        float s = LinearToSrgb(linear);
        int v = (int)(s * 255f + 0.5f);
        return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
    }

    // LUT nhanh linear[0..1] -> byte sRGB (4096 mức). Tránh MathF.Pow mỗi pixel khi encode preview.
    private const int FastLutSize = 4096;
    private static readonly byte[] LinearToSrgbByteLut = BuildLinearToSrgbByteLut();

    private static byte[] BuildLinearToSrgbByteLut()
    {
        var lut = new byte[FastLutSize + 1];
        for (int i = 0; i <= FastLutSize; i++)
        {
            float lin = i / (float)FastLutSize;
            lut[i] = EncodeByte(lin);
        }
        return lut;
    }

    /// <summary>
    /// Encode nhanh linear -> byte sRGB qua LUT 4096 mức. Dùng cho encode preview hàng triệu pixel.
    /// Sai số &lt;=1 mức 8-bit so với EncodeByte; out-of-range tự clamp.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte EncodeByteFast(float linear)
    {
        if (linear <= 0f) return 0;
        if (linear >= 1f) return 255;
        int idx = (int)(linear * FastLutSize + 0.5f);
        return LinearToSrgbByteLut[idx];
    }

    /// <summary>Encode 1 kênh linear -> ushort sRGB 16-bit, có clamp.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort EncodeUShort(float linear)
    {
        float s = LinearToSrgb(linear);
        int v = (int)(s * 65535f + 0.5f);
        return (ushort)(v < 0 ? 0 : (v > 65535 ? 65535 : v));
    }

    // --- Luminance (theo trọng số Rec.709, dùng trên dữ liệu LINEAR) ---
    public const float LumR = 0.2126f;
    public const float LumG = 0.7152f;
    public const float LumB = 0.0722f;

    /// <summary>Độ sáng cảm nhận (luminance) của pixel linear theo Rec.709.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Luminance(float r, float g, float b) => LumR * r + LumG * g + LumB * b;
}
