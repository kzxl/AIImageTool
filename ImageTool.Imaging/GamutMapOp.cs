using System;
using System.Collections.Generic;

namespace ImageTool.Imaging;

/// <summary>
/// Gamut mapping (#1, nền cho soft-proofing): đưa màu working (linear sRGB) về NẰM TRONG gamut
/// đích (sRGB/AdobeRGB/Rec2020/DisplayP3 hoặc ma trận ICC bất kỳ qua DestMatrix). Màu ngoài gamut
/// đích bị (a) Clip = kẹp về [0,1] sau khi chuyển sang đích, hoặc (b) Desaturate = kéo về phía
/// luminance (giữ độ sáng) cho tới khi nằm trong gamut. Kết quả vẫn trả về working linear sRGB nên
/// pipeline phía sau không đổi — đây là "soft proof bake": ảnh hiển thị đúng giới hạn thiết bị đích.
///
/// DestMatrix (RGB->XYZ D65 của gamut đích, 9 phần tử) khi != null sẽ ưu tiên hơn Dest enum, cho
/// phép proof theo profile ICC thật. Thuần ma trận -> unit test được, không cần ICC/native.
/// </summary>
public sealed class GamutMapOp : IEditOp
{
    public const string Type = "GamutMap";
    public string OpType => Type;

    public enum Mode { Clip, Desaturate }

    public ColorSpaces.Space Dest = ColorSpaces.Space.Srgb;
    public Mode Method = Mode.Clip;
    /// <summary>Ma trận RGB->XYZ (D65) của gamut đích nếu proof theo ICC thật (ưu tiên hơn Dest).</summary>
    public float[]? DestMatrix;

    /// <summary>sRGB là working space; clip-to-sRGB vẫn có tác dụng (kẹp màu ngoài [0,1]).</summary>
    public bool IsIdentity => false;

    public void Apply(LinearImage image, float scale)
    {
        // M: working linear sRGB -> đích; Minv: đích -> working.
        float[] destToXyz = DestMatrix ?? ColorSpaces.RgbToXyzD65(Dest);
        float[] srgbToXyz = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.Srgb);
        float[] toDest = ColorSpaces.Mul3x3(ColorSpaces.Invert3x3(destToXyz), srgbToXyz);
        float[] toWork = ColorSpaces.Invert3x3(toDest);

        float a00 = toDest[0], a01 = toDest[1], a02 = toDest[2];
        float a10 = toDest[3], a11 = toDest[4], a12 = toDest[5];
        float a20 = toDest[6], a21 = toDest[7], a22 = toDest[8];
        float b00 = toWork[0], b01 = toWork[1], b02 = toWork[2];
        float b10 = toWork[3], b11 = toWork[4], b12 = toWork[5];
        float b20 = toWork[6], b21 = toWork[7], b22 = toWork[8];
        var method = Method;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            // sang gamut đích.
            float dr = a00 * r + a01 * g + a02 * b;
            float dg = a10 * r + a11 * g + a12 * b;
            float db = a20 * r + a21 * g + a22 * b;

            if (method == Mode.Clip)
            {
                dr = Clamp01(dr); dg = Clamp01(dg); db = Clamp01(db);
            }
            else // Desaturate: kéo về luminance cho tới khi nằm trong [0,1].
            {
                float lum = ColorSpace.Luminance(dr, dg, db);
                lum = Clamp01(lum);
                float t = OutOfGamutBlend(dr, dg, db);
                if (t > 0f)
                {
                    dr = dr + (lum - dr) * t;
                    dg = dg + (lum - dg) * t;
                    db = db + (lum - db) * t;
                    // còn dư ngoài biên do làm tròn -> clip nhẹ.
                    dr = Clamp01(dr); dg = Clamp01(dg); db = Clamp01(db);
                }
            }

            // về lại working linear sRGB.
            float nr = b00 * dr + b01 * dg + b02 * db;
            float ng = b10 * dr + b11 * dg + b12 * db;
            float nb = b20 * dr + b21 * dg + b22 * db;
            r = nr < 0f ? 0f : nr;
            g = ng < 0f ? 0f : ng;
            b = nb < 0f ? 0f : nb;
        });
    }

    /// <summary>Mức "ngoài gamut" [0..1]: 0 = trong gamut; càng lớn càng cần kéo về luminance.</summary>
    private static float OutOfGamutBlend(float r, float g, float b)
    {
        float over = 0f;
        over = MathF.Max(over, -r); over = MathF.Max(over, -g); over = MathF.Max(over, -b);
        over = MathF.Max(over, r - 1f); over = MathF.Max(over, g - 1f); over = MathF.Max(over, b - 1f);
        if (over <= 0f) return 0f;
        // chuẩn hoá: lệch 0.5 trở lên -> kéo gần hết.
        return Math.Clamp(over / 0.5f, 0f, 1f);
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    public Dictionary<string, string> ToParams()
    {
        var d = new Dictionary<string, string>
        {
            ["dest"] = ColorSpaces.Name(Dest),
            ["method"] = Method == Mode.Desaturate ? "desaturate" : "clip",
        };
        if (DestMatrix != null && DestMatrix.Length == 9)
            d["destMatrix"] = string.Join(",", Array.ConvertAll(DestMatrix, x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        return d;
    }

    public static GamutMapOp FromParams(IReadOnlyDictionary<string, string> p)
    {
        ColorSpaces.TryParse(EditOpRegistry.S(p, "dest"), out var dest);
        var op = new GamutMapOp
        {
            Dest = dest,
            Method = EditOpRegistry.S(p, "method") == "desaturate" ? Mode.Desaturate : Mode.Clip,
        };
        string mx = EditOpRegistry.S(p, "destMatrix");
        if (!string.IsNullOrWhiteSpace(mx))
        {
            var parts = mx.Split(',');
            if (parts.Length == 9)
            {
                var m = new float[9];
                bool ok = true;
                for (int i = 0; i < 9; i++)
                    ok &= float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out m[i]);
                if (ok) op.DestMatrix = m;
            }
        }
        return op;
    }

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}

/// <summary>
/// Phân tích gamut (#1 soft-proof warning): kiểm tra 1 màu working (linear sRGB) có nằm trong gamut
/// đích không, và đo % pixel ngoài gamut của 1 ảnh. Dùng cho cảnh báo "màu này không in/hiển thị
/// được trên thiết bị đích". Thuần ma trận -> test được.
/// </summary>
public static class GamutCheck
{
    /// <summary>True nếu màu (linear sRGB) nằm NGOÀI gamut đích (1 kênh &lt;0 hoặc &gt;1 sau khi chuyển).</summary>
    public static bool IsOutOfGamut(float r, float g, float b, ColorSpaces.Space dest, float tol = 1e-4f)
        => IsOutOfGamut(r, g, b, ColorSpaces.RgbToXyzD65(dest), tol);

    /// <summary>Biến thể nhận ma trận RGB->XYZ (D65) của gamut đích (vd từ ICC thật).</summary>
    public static bool IsOutOfGamut(float r, float g, float b, float[] destToXyz, float tol = 1e-4f)
    {
        float[] srgbToXyz = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.Srgb);
        float[] toDest = ColorSpaces.Mul3x3(ColorSpaces.Invert3x3(destToXyz), srgbToXyz);
        float dr = toDest[0] * r + toDest[1] * g + toDest[2] * b;
        float dg = toDest[3] * r + toDest[4] * g + toDest[5] * b;
        float db = toDest[6] * r + toDest[7] * g + toDest[8] * b;
        return dr < -tol || dg < -tol || db < -tol || dr > 1f + tol || dg > 1f + tol || db > 1f + tol;
    }

    /// <summary>Tỉ lệ [0..1] số pixel ngoài gamut đích trong ảnh (bỏ qua alpha).</summary>
    public static float OutOfGamutFraction(LinearImage img, ColorSpaces.Space dest)
    {
        float[] destToXyz = ColorSpaces.RgbToXyzD65(dest);
        float[] srgbToXyz = ColorSpaces.RgbToXyzD65(ColorSpaces.Space.Srgb);
        float[] toDest = ColorSpaces.Mul3x3(ColorSpaces.Invert3x3(destToXyz), srgbToXyz);
        float a0 = toDest[0], a1 = toDest[1], a2 = toDest[2];
        float a3 = toDest[3], a4 = toDest[4], a5 = toDest[5];
        float a6 = toDest[6], a7 = toDest[7], a8 = toDest[8];
        float[] px = img.Pixels;
        int n = img.PixelCount;
        int outCount = 0;
        const float tol = 1e-4f;
        for (int i = 0; i < n; i++)
        {
            int p = i * 4;
            float r = px[p], g = px[p + 1], b = px[p + 2];
            float dr = a0 * r + a1 * g + a2 * b;
            float dg = a3 * r + a4 * g + a5 * b;
            float db = a6 * r + a7 * g + a8 * b;
            if (dr < -tol || dg < -tol || db < -tol || dr > 1f + tol || dg > 1f + tol || db > 1f + tol)
                outCount++;
        }
        return n == 0 ? 0f : (float)outCount / n;
    }
}
