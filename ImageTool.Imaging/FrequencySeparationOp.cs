using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Frequency Separation (#7) — tách ảnh thành tầng TẦN THẤP (màu/tông, blur) và TẦN CAO (chi tiết,
/// = gốc − blur), rồi:
///   - Smooth tầng thấp (giảm loang màu/đốm da) theo <see cref="Smoothing"/>;
///   - Giữ/khuếch chi tiết tầng cao theo <see cref="DetailAmount"/>.
/// Kết quả = lowSmoothed + highScaled. Dùng cho retouch da: làm mịn màu nhưng giữ kết cấu lỗ chân lông.
///
/// Bán kính tách (Radius px) nhân theo scale để khớp proxy/full-res. Chạy trên RGB linear.
/// </summary>
public sealed class FrequencySeparationOp : IEditOp
{
    public const string Type = "FreqSep";
    public string OpType => Type;

    public float Radius = 8f;        // bán kính tách (px @ full-res)
    public float Smoothing;          // 0..1: làm mịn thêm tầng thấp (blur mạnh hơn)
    public float DetailAmount = 1f;  // hệ số tầng cao (1 = giữ nguyên, >1 = nét hơn, <1 = mềm)

    public bool IsIdentity => Smoothing <= 0f && MathF.Abs(DetailAmount - 1f) < 1e-3f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        int n = w * h;
        float[] px = image.Pixels;
        float radius = MathF.Max(1f, Radius * scale);
        float s = Math.Clamp(Smoothing, 0f, 1f);
        float da = DetailAmount;

        for (int c = 0; c < 3; c++)
        {
            // Lấy kênh ra mảng phẳng.
            var ch = new float[n];
            for (int i = 0; i < n; i++) ch[i] = px[i * 4 + c];

            // Tách: lowBase = blur(ch); high = ch - lowBase.
            float[] lowBase = GaussianBlur.BlurPlane(ch, w, h, radius);

            // Làm mịn thêm tầng thấp (pha lowBase với blur mạnh hơn theo Smoothing).
            float[] lowFinal = lowBase;
            if (s > 0f)
            {
                float extra = radius * (1f + 2f * s);
                float[] lowSmooth = GaussianBlur.BlurPlane(lowBase, w, h, extra);
                lowFinal = new float[n];
                for (int i = 0; i < n; i++) lowFinal[i] = lowBase[i] + (lowSmooth[i] - lowBase[i]) * s;
            }

            // Tái hợp: out = lowFinal + (ch - lowBase) * detail.
            for (int i = 0; i < n; i++)
            {
                float high = ch[i] - lowBase[i];
                float v = lowFinal[i] + high * da;
                px[i * 4 + c] = v < 0f ? 0f : v;
            }
        }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["radius"] = F(Radius), ["smooth"] = F(Smoothing), ["detail"] = F(DetailAmount),
    };

    public static FrequencySeparationOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Radius = EditOpRegistry.F(p, "radius", 8f),
        Smoothing = EditOpRegistry.F(p, "smooth"),
        DetailAmount = EditOpRegistry.F(p, "detail", 1f),
    };

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}
