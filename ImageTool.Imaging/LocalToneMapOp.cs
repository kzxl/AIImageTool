using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Local tone mapping (HDR-look cho ảnh single-shot, kiểu local Laplacian / detail-preserving tone
/// compression). Phân tách LOG-luminance thành: base = bản blur bán kính lớn (độ sáng tổng thể) +
/// detail = phần còn lại (tương phản cục bộ / vi chi tiết). Nén DẢI ĐỘNG bằng cách thu hẹp base
/// (kéo vùng sáng/tối lại gần midtone) trong khi GIỮ (hoặc tăng nhẹ) detail -> ảnh "mở bóng, ghìm
/// sáng" mà vẫn nổi khối, không bẹt như kéo Shadows/Highlights toàn cục.
///
/// Pixel-radius nên scale-aware (bán kính base nhân theo scale để proxy khớp full-res). Áp trên
/// LUMINANCE rồi scale RGB theo tỉ lệ (giữ hue/sat). Thuần toán -> unit test được.
/// </summary>
public sealed class LocalToneMapOp : IEditOp
{
    public const string Type = "LocalToneMap";
    public string OpType => Type;

    /// <summary>Độ nén dải động [0..1]: 0 = không đổi, 1 = nén mạnh base về midtone.</summary>
    public float Amount = 0f;
    /// <summary>Tăng/giảm tương phản cục bộ (detail) [-1..1]: dương = nổi chi tiết hơn.</summary>
    public float Detail = 0f;
    /// <summary>Bán kính base (px ở full-res). Lớn = tách khối to/độ sáng tổng thể.</summary>
    public float BaseRadius = 80f;

    public bool IsIdentity => MathF.Abs(Amount) < 1e-4f && MathF.Abs(Detail) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        int w = image.Width, h = image.Height;
        float[] px = image.Pixels;

        // 1) log-luminance plane.
        var logLum = new float[w * h];
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int p = (row + x) * 4;
                float lum = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
                logLum[row + x] = MathF.Log(MathF.Max(lum, 1e-4f));
            }
        });

        // 2) base = blur log-luminance (bán kính lớn, scale-aware); detail = log - base.
        float radius = MathF.Max(1f, BaseRadius * scale);
        var baseLog = GaussianBlur.BlurPlane(logLum, w, h, radius);

        float amt = Math.Clamp(Amount, 0f, 1f);
        // hệ số nén base: 1 = giữ nguyên, càng nhỏ càng nén. Nén tối đa ~0.35 ở amount=1.
        float baseScale = 1f - 0.65f * amt;
        float detailGain = 1f + Math.Clamp(Detail, -1f, 1f);

        // 3) tái dựng log-luminance: giữ trung bình base, nén độ lệch base + giữ/đẩy detail.
        //    Lấy mốc = base (độ sáng tổng thể local) để nén quanh chính nó -> không trôi tông tổng thể.
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                int p = idx * 4;
                float lum = ColorSpace.Luminance(px[p], px[p + 1], px[p + 2]);
                if (lum < 1e-6f) continue;

                float b = baseLog[idx];
                float detail = logLum[idx] - b;
                // nén base quanh điểm neo midtone (log của 0.18 ~ giữa), giữ detail.
                const float anchor = -1.7148f; // ln(0.18)
                float newBase = anchor + (b - anchor) * baseScale;
                float newLog = newBase + detail * detailGain;
                float newLum = MathF.Exp(newLog);

                float gain = newLum / lum;
                // chặn gain cực đoan để tránh vỡ màu.
                if (gain < 0f) gain = 0f;
                else if (gain > 8f) gain = 8f;
                px[p] *= gain; px[p + 1] *= gain; px[p + 2] *= gain;
            }
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["amount"] = F(Amount), ["detail"] = F(Detail), ["radius"] = F(BaseRadius),
    };
    private static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    public static LocalToneMapOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Amount = EditOpRegistry.F(p, "amount"),
        Detail = EditOpRegistry.F(p, "detail"),
        BaseRadius = EditOpRegistry.F(p, "radius", 80f),
    };

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
