using System;

namespace ImageTool.Imaging;

/// <summary>
/// Tập hợp các op chỉnh sửa cơ bản (Basic panel), tất cả chạy trong LINEAR LIGHT.
/// Mỗi op là pixel-wise (bỏ qua scale) nên preview proxy và full-res cho kết quả nhất quán.
/// EditOpRegistry.CreateDefault() gọi RegisterAll để nạp toàn bộ.
/// </summary>
public static class BasicOps
{
    public static void RegisterAll(EditOpRegistry reg)
    {
        reg.Register("Exposure", p => new ExposureOp(EditOpRegistry.F(p, "ev", 0f)));
        reg.Register("Contrast", p => new ContrastOp(EditOpRegistry.F(p, "amount", 0f)));
        reg.Register("Saturation", p => new SaturationOp(EditOpRegistry.F(p, "amount", 0f)));
        reg.Register("WhiteBalance", p => new WhiteBalanceOp(
            EditOpRegistry.F(p, "temp", 0f),
            EditOpRegistry.F(p, "tint", 0f)));
        reg.Register("Brightness", p => new BrightnessOp(EditOpRegistry.F(p, "amount", 0f)));
    }
}

/// <summary>Exposure: nhân tuyến tính theo stops (EV). +1 EV = gấp đôi ánh sáng.</summary>
public sealed class ExposureOp : IEditOp
{
    private readonly float _gain;
    public ExposureOp(float ev) => _gain = MathF.Pow(2f, ev);
    public string OpType => "Exposure";
    public void Apply(LinearImage image, float scale)
    {
        float g = _gain;
        image.ProcessPixels((ref float r, ref float gg, ref float b, ref float a) =>
        {
            r *= g; gg *= g; b *= g;
        });
    }
}

/// <summary>Brightness: cộng offset linear nhẹ (amount -1..1 ánh xạ ra dải hẹp).</summary>
public sealed class BrightnessOp : IEditOp
{
    private readonly float _offset;
    public BrightnessOp(float amount) => _offset = Math.Clamp(amount, -1f, 1f) * 0.2f;
    public string OpType => "Brightness";
    public void Apply(LinearImage image, float scale)
    {
        float o = _offset;
        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r += o; g += o; b += o;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }
}

/// <summary>
/// Contrast quanh điểm xám giữa (18% reflectance ~ 0.18 linear). amount -1..1.
/// Kéo dãn/ co dữ liệu quanh pivot bằng hệ số tuyến tính.
/// </summary>
public sealed class ContrastOp : IEditOp
{
    private const float Pivot = 0.18f;
    private readonly float _factor;
    public ContrastOp(float amount) => _factor = 1f + Math.Clamp(amount, -1f, 1f);
    public string OpType => "Contrast";
    public void Apply(LinearImage image, float scale)
    {
        float f = _factor;
        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r = (r - Pivot) * f + Pivot;
            g = (g - Pivot) * f + Pivot;
            b = (b - Pivot) * f + Pivot;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }
}

/// <summary>
/// Saturation: nội suy giữa luminance (Rec.709 trên linear) và màu gốc. amount -1..1.
/// amount = -1 -> grayscale, 0 -> giữ nguyên, &gt;0 -> tăng độ bão hoà.
/// </summary>
public sealed class SaturationOp : IEditOp
{
    private readonly float _amount;
    public SaturationOp(float amount) => _amount = 1f + Math.Clamp(amount, -1f, 1f);
    public string OpType => "Saturation";
    public void Apply(LinearImage image, float scale)
    {
        float s = _amount;
        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            float lum = ColorSpace.Luminance(r, g, b);
            r = lum + (r - lum) * s;
            g = lum + (g - lum) * s;
            b = lum + (b - lum) * s;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }
}

/// <summary>
/// White balance đơn giản qua kênh gain. temp -1..1 (ấm/lạnh: R lên, B xuống và ngược lại),
/// tint -1..1 (xanh lá / tím: G xuống/lên). Bảo toàn độ sáng tương đối ở mức cơ bản.
/// </summary>
public sealed class WhiteBalanceOp : IEditOp
{
    private readonly float _rGain, _gGain, _bGain;
    public WhiteBalanceOp(float temp, float tint)
    {
        temp = Math.Clamp(temp, -1f, 1f);
        tint = Math.Clamp(tint, -1f, 1f);
        _rGain = 1f + temp * 0.3f;
        _bGain = 1f - temp * 0.3f;
        _gGain = 1f + tint * 0.3f;
    }
    public string OpType => "WhiteBalance";
    public void Apply(LinearImage image, float scale)
    {
        float rg = _rGain, gg = _gGain, bg = _bGain;
        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r *= rg; g *= gg; b *= bg;
            if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
        });
    }
}
