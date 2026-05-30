using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Tone Curve kiểu Lightroom/Darktable. Một đường cong "master" (RGB) áp lên cả 3 kênh,
/// cộng 3 đường cong riêng cho R/G/B. Điểm điều khiển nằm trong không gian sRGB-perceptual
/// [0..1] (đúng cách mắt nhìn curve), nội suy bằng spline đơn điệu (monotone cubic) để không
/// bị overshoot. Biến đổi: linear -> sRGB -> áp curve -> linear.
///
/// <para>Chế độ <see cref="PreserveHue"/> (D1.4): khi bật, đường master KHÔNG áp per-channel
/// (gây dịch hue ở màu rực) mà áp lên LUMINANCE rồi scale RGB theo tỉ lệ -> giữ nguyên hue và
/// tỉ lệ bão hoà. Các đường R/G/B riêng vẫn áp per-channel sau đó (như cũ).</para>
///
/// Tham số serialize: "rgb" / "r" / "g" / "b" = chuỗi "x0,y0;x1,y1;..." (điểm sắp theo x tăng).
/// Mặc định (identity) = "0,0;1,1". "preserveHue" = "true"/"false".
/// </summary>
public sealed class ToneCurveOp : IEditOp
{
    public const string Type = "ToneCurve";
    public string OpType => Type;

    private readonly Curve _rgb;
    private readonly Curve _r;
    private readonly Curve _g;
    private readonly Curve _b;

    /// <summary>D1.4: master curve áp theo luminance (giữ hue) thay vì per-channel.</summary>
    public bool PreserveHue { get; set; }

    public ToneCurveOp(IReadOnlyList<(float x, float y)>? rgb = null,
                       IReadOnlyList<(float x, float y)>? r = null,
                       IReadOnlyList<(float x, float y)>? g = null,
                       IReadOnlyList<(float x, float y)>? b = null)
    {
        _rgb = new Curve(rgb);
        _r = new Curve(r);
        _g = new Curve(g);
        _b = new Curve(b);
    }

    public bool IsIdentity => _rgb.IsIdentity && _r.IsIdentity && _g.IsIdentity && _b.IsIdentity;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        var rgb = _rgb; var rr = _r; var gg = _g; var bb = _b;
        bool preserve = PreserveHue && !rgb.IsIdentity;

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float sr = ColorSpace.LinearToSrgb(r);
            float sg = ColorSpace.LinearToSrgb(g);
            float sb = ColorSpace.LinearToSrgb(b);

            if (preserve)
            {
                // áp master lên luminance (sRGB perceptual), scale theo tỉ lệ giữ hue.
                float lin = ColorSpace.LumR * sr + ColorSpace.LumG * sg + ColorSpace.LumB * sb;
                if (lin > 1e-5f)
                {
                    float lout = rgb.Eval(lin);
                    float ratio = lout / lin;
                    sr = Math.Clamp(sr * ratio, 0f, 1f);
                    sg = Math.Clamp(sg * ratio, 0f, 1f);
                    sb = Math.Clamp(sb * ratio, 0f, 1f);
                }
                else
                {
                    float lout = rgb.Eval(0f);
                    sr = sg = sb = lout;
                }
            }
            else
            {
                // master per-channel (mặc định, như cũ).
                sr = rgb.Eval(sr); sg = rgb.Eval(sg); sb = rgb.Eval(sb);
            }

            // per-channel R/G/B luôn áp sau.
            sr = rr.Eval(sr); sg = gg.Eval(sg); sb = bb.Eval(sb);

            r = ColorSpace.SrgbToLinear(sr);
            g = ColorSpace.SrgbToLinear(sg);
            b = ColorSpace.SrgbToLinear(sb);
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["rgb"] = _rgb.Serialize(),
        ["r"] = _r.Serialize(),
        ["g"] = _g.Serialize(),
        ["b"] = _b.Serialize(),
        ["preserveHue"] = PreserveHue ? "true" : "false",
    };

    public static ToneCurveOp FromParams(IReadOnlyDictionary<string, string> p) => new(
        Curve.Parse(EditOpRegistry.S(p, "rgb")),
        Curve.Parse(EditOpRegistry.S(p, "r")),
        Curve.Parse(EditOpRegistry.S(p, "g")),
        Curve.Parse(EditOpRegistry.S(p, "b")))
    {
        PreserveHue = EditOpRegistry.B(p, "preserveHue"),
    };

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);

    /// <summary>1 đường cong với LUT 256 mức + nội suy monotone-cubic giữa các điểm.</summary>
    private sealed class Curve
    {
        private readonly float[] _lut; // 256 mức [0..1]
        private readonly string _serialized;
        public bool IsIdentity { get; }

        public Curve(IReadOnlyList<(float x, float y)>? points)
        {
            var pts = CurveMath.Normalize(points);
            IsIdentity = CurveMath.IsIdentity(pts);
            _lut = CurveMath.BuildLut(pts);
            _serialized = CurveMath.Serialize(pts);
        }

        public float Eval(float x) => CurveMath.Eval(_lut, x);

        public string Serialize() => _serialized;

        public static List<(float x, float y)>? Parse(string s) => CurveMath.Parse(s);
    }
}
