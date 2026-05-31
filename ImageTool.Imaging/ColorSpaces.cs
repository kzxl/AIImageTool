using System;

namespace ImageTool.Imaging;

/// <summary>
/// Ma trận chuyển không gian màu RGB (cùng white point D65) cho D2.2 (working/input color space).
/// Pipeline làm việc ở LINEAR sRGB (primaries Rec.709). Nếu ảnh được mã hoá theo gamut rộng hơn
/// (AdobeRGB / Rec2020 / Display P3) mà bị diễn giải nhầm là sRGB, màu sẽ bị lệch. InputProfileOp
/// dùng các ma trận ở đây để chuyển linear RGB nguồn -> linear sRGB (working) bằng 1 phép nhân 3x3.
///
/// Tất cả D65 nên không cần chromatic adaptation. Thuần toán học -> unit test được, không cần native.
/// </summary>
public static class ColorSpaces
{
    public enum Space { Srgb, AdobeRgb, Rec2020, DisplayP3 }

    // RGB(linear) -> XYZ (D65) cho từng không gian.
    private static readonly float[] SrgbToXyz =
    {
        0.4124564f, 0.3575761f, 0.1804375f,
        0.2126729f, 0.7151522f, 0.0721750f,
        0.0193339f, 0.1191920f, 0.9503041f,
    };
    private static readonly float[] XyzToSrgb =
    {
        3.2404542f, -1.5371385f, -0.4985314f,
        -0.9692660f, 1.8760108f, 0.0415560f,
        0.0556434f, -0.2040259f, 1.0572252f,
    };
    private static readonly float[] AdobeToXyz =
    {
        0.5767309f, 0.1855540f, 0.1881852f,
        0.2973769f, 0.6273491f, 0.0752741f,
        0.0270343f, 0.0706872f, 0.9911085f,
    };
    private static readonly float[] Rec2020ToXyz =
    {
        0.6369580f, 0.1446169f, 0.1688810f,
        0.2627002f, 0.6779981f, 0.0593017f,
        0.0000000f, 0.0280727f, 1.0609851f,
    };
    private static readonly float[] P3ToXyz =
    {
        0.4865709f, 0.2656677f, 0.1982173f,
        0.2289746f, 0.6917385f, 0.0792869f,
        0.0000000f, 0.0451134f, 1.0439444f,
    };

    private static float[] RgbToXyz(Space s) => s switch
    {
        Space.AdobeRgb => AdobeToXyz,
        Space.Rec2020 => Rec2020ToXyz,
        Space.DisplayP3 => P3ToXyz,
        _ => SrgbToXyz,
    };

    /// <summary>Ma trận RGB(linear)->XYZ (D65, row-major) của không gian <paramref name="s"/>. Dùng để ghi ICC.</summary>
    public static float[] RgbToXyzD65(Space s) => (float[])RgbToXyz(s).Clone();

    /// <summary>Ma trận RGB(linear)->XYZ quy về D50 (PCS của ICC) bằng Bradford adaptation. Dùng ghi colorant ICC.</summary>
    public static float[] RgbToXyzD50(Space s) => Mul3x3(BradfordAdaptation(D65White, D50White), RgbToXyz(s));

    /// <summary>Ma trận 3x3 (row-major) chuyển linear RGB từ <paramref name="from"/> sang <paramref name="to"/>.</summary>
    public static float[] ConversionMatrix(Space from, Space to)
    {
        if (from == to) return Identity();
        // M = (XYZ->to) * (from->XYZ)
        float[] fromXyz = RgbToXyz(from);
        float[] xyzTo = to == Space.Srgb ? XyzToSrgb : Invert3x3(RgbToXyz(to));
        return Mul3x3(xyzTo, fromXyz);
    }

    /// <summary>Chuyển linear RGB nguồn -> linear sRGB (working). Tiện dụng cho InputProfileOp.</summary>
    public static float[] ToWorkingMatrix(Space from) => ConversionMatrix(from, Space.Srgb);

    public static bool TryParse(string? name, out Space space)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "adobergb": case "adobe": case "argb": space = Space.AdobeRgb; return true;
            case "rec2020": case "bt2020": case "2020": space = Space.Rec2020; return true;
            case "displayp3": case "p3": case "dci-p3": space = Space.DisplayP3; return true;
            case "srgb": case "": space = Space.Srgb; return true;
            default: space = Space.Srgb; return false;
        }
    }

    public static string Name(Space s) => s switch
    {
        Space.AdobeRgb => "AdobeRGB",
        Space.Rec2020 => "Rec2020",
        Space.DisplayP3 => "DisplayP3",
        _ => "sRGB",
    };

    // --- Phân loại gamut từ ma trận RGB->XYZ thật (D2.2/7.3: parse colorant ICC) ---
    // White point chuẩn (XYZ, Y=1). ICC PCS dùng D50; các ma trận trong file này dùng D65.
    private static readonly float[] D50White = { 0.96422f, 1.00000f, 0.82521f };
    private static readonly float[] D65White = { 0.95047f, 1.00000f, 1.08883f };
    // Ma trận Bradford (cone response) + nghịch đảo.
    private static readonly float[] Bradford =
    {
         0.8951000f,  0.2664000f, -0.1614000f,
        -0.7502000f,  1.7135000f,  0.0367000f,
         0.0389000f, -0.0685000f,  1.0296000f,
    };

    /// <summary>
    /// Ma trận chromatic adaptation Bradford từ white point <paramref name="srcWhite"/> sang
    /// <paramref name="dstWhite"/> (XYZ, Y=1). Áp cho 1 ma trận RGB->XYZ để đổi white point.
    /// </summary>
    public static float[] BradfordAdaptation(float[] srcWhite, float[] dstWhite)
    {
        float[] bInv = Invert3x3(Bradford);
        float[] src = Mul3x1(Bradford, srcWhite);  // cone response nguồn
        float[] dst = Mul3x1(Bradford, dstWhite);  // cone response đích
        float[] diag =
        {
            dst[0] / src[0], 0, 0,
            0, dst[1] / src[1], 0,
            0, 0, dst[2] / src[2],
        };
        return Mul3x3(bInv, Mul3x3(diag, Bradford));
    }

    /// <summary>
    /// Quy ma trận RGB->XYZ tham chiếu D50 (colorant ICC, PCS D50) về D65 (working space của pipeline).
    /// </summary>
    public static float[] AdaptD50ToD65(float[] rgbToXyzD50)
        => Mul3x3(BradfordAdaptation(D50White, D65White), rgbToXyzD50);

    /// <summary>
    /// Tìm không gian màu quen thuộc khớp NHẤT với ma trận RGB->XYZ (D65) đã cho, theo tổng sai số
    /// tuyệt đối từng phần tử. Trả null nếu sai số nhỏ nhất vẫn vượt <paramref name="tolerance"/>
    /// (profile lạ -> để người dùng tự chọn). Dùng khi ICC không có tên nhận diện được nhưng có
    /// colorant matrix.
    /// </summary>
    public static Space? MatchSpace(float[] rgbToXyzD65, float tolerance = 0.02f)
    {
        if (rgbToXyzD65 == null || rgbToXyzD65.Length != 9) return null;
        Space best = Space.Srgb;
        float bestErr = float.MaxValue;
        foreach (Space s in new[] { Space.Srgb, Space.AdobeRgb, Space.Rec2020, Space.DisplayP3 })
        {
            float[] @ref = RgbToXyz(s);
            float err = 0f;
            for (int i = 0; i < 9; i++) err += MathF.Abs(rgbToXyzD65[i] - @ref[i]);
            if (err < bestErr) { bestErr = err; best = s; }
        }
        return bestErr <= tolerance ? best : (Space?)null;
    }

    /// <summary>Nhân ma trận 3x3 (row-major) với vector 3.</summary>
    public static float[] Mul3x1(float[] m, float[] v) => new float[]
    {
        m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
        m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
        m[6] * v[0] + m[7] * v[1] + m[8] * v[2],
    };

    private static float[] Identity() => new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

    /// <summary>Nhân 2 ma trận 3x3 (row-major): a*b.</summary>
    public static float[] Mul3x3(float[] a, float[] b)
    {
        var r = new float[9];
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                r[row * 3 + col] =
                    a[row * 3 + 0] * b[0 * 3 + col] +
                    a[row * 3 + 1] * b[1 * 3 + col] +
                    a[row * 3 + 2] * b[2 * 3 + col];
        return r;
    }

    /// <summary>Nghịch đảo ma trận 3x3 (row-major).</summary>
    public static float[] Invert3x3(float[] m)
    {
        float a = m[0], b = m[1], c = m[2];
        float d = m[3], e = m[4], f = m[5];
        float g = m[6], h = m[7], i = m[8];
        float A = e * i - f * h;
        float B = -(d * i - f * g);
        float C = d * h - e * g;
        float det = a * A + b * B + c * C;
        if (MathF.Abs(det) < 1e-12f) return Identity();
        float inv = 1f / det;
        return new float[]
        {
            A * inv,                 -(b * i - c * h) * inv,  (b * f - c * e) * inv,
            B * inv,                  (a * i - c * g) * inv, -(a * f - c * d) * inv,
            C * inv,                 -(a * h - b * g) * inv,  (a * e - b * d) * inv,
        };
    }
}
