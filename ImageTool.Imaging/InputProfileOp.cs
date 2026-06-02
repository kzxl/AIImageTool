using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Input color profile / working space (D2.2): diễn giải ảnh nguồn theo 1 gamut rộng (AdobeRGB /
/// Rec2020 / Display P3) và chuyển về không gian làm việc LINEAR sRGB bằng ma trận 3x3 (D65, không
/// cần chromatic adaptation). Đặt SỚM trong pipeline (trước các op màu) để mọi chỉnh sửa diễn ra
/// đúng trên dữ liệu đã quy về working space.
///
/// Profile = "sRGB" -> no-op. Pixel ngoài gamut sRGB sau chuyển có thể âm -> clamp về 0 (giữ
/// headroom dương cho highlight). Thuần ma trận -> test được, không cần ICC/native.
/// </summary>
public sealed class InputProfileOp : IEditOp
{
    public const string Type = "InputProfile";
    public string OpType => Type;

    public ColorSpaces.Space Source = ColorSpaces.Space.Srgb;

    /// <summary>
    /// Ma trận RGB(linear nguồn)->XYZ (D65) tuỳ chỉnh (9 phần tử) — vd colorant matrix từ ICC nhúng
    /// hoặc camera matrix (DCP/DNG ColorMatrix đã chuẩn hoá về D65). Khi != null, ƯU TIÊN hơn Source:
    /// quy nguồn -> working sRGB bằng (XYZ->sRGB) * (nguồn->XYZ). Nền tảng cho DCP/camera profile.
    /// </summary>
    public float[]? SourceMatrix;

    public bool IsIdentity => SourceMatrix == null && Source == ColorSpaces.Space.Srgb;

    private float[] WorkingMatrix()
    {
        if (SourceMatrix != null && SourceMatrix.Length == 9)
        {
            // (XYZ -> sRGB) * (nguồn -> XYZ).
            float[] xyzToSrgb = ColorSpaces.Invert3x3(ColorSpaces.RgbToXyzD65(ColorSpaces.Space.Srgb));
            return ColorSpaces.Mul3x3(xyzToSrgb, SourceMatrix);
        }
        return ColorSpaces.ToWorkingMatrix(Source);
    }

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float[] m = WorkingMatrix();
        float m00 = m[0], m01 = m[1], m02 = m[2];
        float m10 = m[3], m11 = m[4], m12 = m[5];
        float m20 = m[6], m21 = m[7], m22 = m[8];

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float nr = m00 * r + m01 * g + m02 * b;
            float ng = m10 * r + m11 * g + m12 * b;
            float nb = m20 * r + m21 * g + m22 * b;
            r = nr < 0f ? 0f : nr;
            g = ng < 0f ? 0f : ng;
            b = nb < 0f ? 0f : nb;
        });
    }

    public Dictionary<string, string> ToParams()
    {
        var d = new Dictionary<string, string> { ["space"] = ColorSpaces.Name(Source) };
        if (SourceMatrix != null && SourceMatrix.Length == 9)
            d["srcMatrix"] = string.Join(",", System.Array.ConvertAll(SourceMatrix,
                x => x.ToString("R", CultureInfo.InvariantCulture)));
        return d;
    }

    public static InputProfileOp FromParams(IReadOnlyDictionary<string, string> p)
    {
        ColorSpaces.TryParse(EditOpRegistry.S(p, "space"), out var s);
        var op = new InputProfileOp { Source = s };
        string mx = EditOpRegistry.S(p, "srcMatrix");
        if (!string.IsNullOrWhiteSpace(mx))
        {
            var parts = mx.Split(',');
            if (parts.Length == 9)
            {
                var m = new float[9];
                bool ok = true;
                for (int i = 0; i < 9; i++)
                    ok &= float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out m[i]);
                if (ok) op.SourceMatrix = m;
            }
        }
        return op;
    }
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
