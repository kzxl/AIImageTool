using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Đảo ảnh (negative) — 13.3. Hữu ích cho workflow scan phim âm bản. Đảo trong sRGB
/// (out = 1 - in trên giá trị gamma-encoded) vì negative phim định nghĩa theo mật độ
/// gamma, không phải linear. Tuỳ chọn chỉ đảo luminance (giữ màu) hiếm dùng nên bỏ qua.
/// </summary>
public sealed class InvertOp : IEditOp
{
    public const string Type = "Invert";
    public string OpType => Type;

    public bool Enabled;

    public bool IsIdentity => !Enabled;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            // linear -> sRGB -> đảo -> linear.
            float sr = 1f - ColorSpace.LinearToSrgb(r);
            float sg = 1f - ColorSpace.LinearToSrgb(g);
            float sb = 1f - ColorSpace.LinearToSrgb(b);
            r = ColorSpace.SrgbToLinear(sr);
            g = ColorSpace.SrgbToLinear(sg);
            b = ColorSpace.SrgbToLinear(sb);
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["enabled"] = Enabled ? "true" : "false",
    };
    public static InvertOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Enabled = EditOpRegistry.B(p, "enabled"),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
