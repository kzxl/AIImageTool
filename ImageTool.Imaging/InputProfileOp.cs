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

    public bool IsIdentity => Source == ColorSpaces.Space.Srgb;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float[] m = ColorSpaces.ToWorkingMatrix(Source);
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

    public Dictionary<string, string> ToParams() => new() { ["space"] = ColorSpaces.Name(Source) };

    public static InputProfileOp FromParams(IReadOnlyDictionary<string, string> p)
    {
        ColorSpaces.TryParse(EditOpRegistry.S(p, "space"), out var s);
        return new InputProfileOp { Source = s };
    }
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
