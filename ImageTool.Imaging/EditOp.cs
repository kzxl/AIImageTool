using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// 1 op chỉnh sửa phi phá hủy: nhận LinearImage và sửa TẠI CHỖ (in place).
/// Op phải thuần tuý theo tham số của nó — cùng tham số + cùng input => cùng output,
/// để pipeline replay được và cache được.
/// </summary>
public interface IEditOp
{
    /// <summary>Khớp với EditOperation.OpType (vd "Exposure", "ToneCurve").</summary>
    string OpType { get; }

    /// <summary>
    /// Áp op lên ảnh (đã ở linear light).
    /// <paramref name="scale"/> = tỉ lệ ảnh hiện tại so với full-res (1.0 = full, 0.5 = proxy 1/2).
    /// Op có bán kính theo pixel (sharpen, vignette) phải nhân bán kính theo scale để preview
    /// và full-res nhất quán. Op pixel-wise (exposure, curve) bỏ qua scale.
    /// </summary>
    void Apply(LinearImage image, float scale);
}

/// <summary>
/// Op làm thay đổi KÍCH THƯỚC ảnh (crop, rotate 90, flip, straighten). Vì LinearImage có
/// Width/Height bất biến, op loại này trả về ảnh MỚI thay vì sửa tại chỗ. EditPipeline phát hiện
/// và thay thế ảnh working bằng kết quả. Tham số toạ độ ở dạng chuẩn hoá [0..1] để khớp scale.
/// </summary>
public interface IResizingOp : IEditOp
{
    LinearImage ApplyResize(LinearImage image, float scale);
}

/// <summary>
/// Tạo IEditOp từ tham số dạng chuỗi (EditOperation.Params). Mỗi op tự đăng ký 1 factory.
/// </summary>
public delegate IEditOp EditOpFactory(IReadOnlyDictionary<string, string> p);

/// <summary>
/// Sổ đăng ký op. Ánh xạ OpType -> factory. Dùng để dựng lại op từ history đã serialize
/// (EditOperation lưu trong IHistoryService / DB) khi mở lại ảnh.
/// </summary>
public sealed class EditOpRegistry
{
    private readonly Dictionary<string, EditOpFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string opType, EditOpFactory factory)
        => _factories[opType] = factory;

    public bool Has(string opType) => _factories.ContainsKey(opType);

    /// <summary>Dựng op từ OpType + params. Trả null nếu OpType chưa đăng ký (bỏ qua khi replay).</summary>
    public IEditOp? Create(string opType, IReadOnlyDictionary<string, string> p)
        => _factories.TryGetValue(opType, out var f) ? f(p) : null;

    // --- Helpers parse tham số chuỗi an toàn (culture-invariant) ---
    public static float F(IReadOnlyDictionary<string, string> p, string key, float def = 0f)
        => p.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    public static int I(IReadOnlyDictionary<string, string> p, string key, int def = 0)
        => p.TryGetValue(key, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    public static bool B(IReadOnlyDictionary<string, string> p, string key, bool def = false)
        => p.TryGetValue(key, out var s) && bool.TryParse(s, out var v) ? v : def;

    public static string S(IReadOnlyDictionary<string, string> p, string key, string def = "")
        => p.TryGetValue(key, out var s) ? s : def;

    /// <summary>Registry mặc định đã nạp toàn bộ op Basic.</summary>
    public static EditOpRegistry CreateDefault()
    {
        var reg = new EditOpRegistry();
        BasicOps.RegisterAll(reg);
        DevelopBasicOp.Register(reg);
        HslMixerOp.Register(reg);
        ToneCurveOp.Register(reg);
        ClarityOp.Register(reg);
        TextureOp.Register(reg);
        SharpenOp.Register(reg);
        VignetteOp.Register(reg);
        ColorGradingOp.Register(reg);
        GrainOp.Register(reg);
        SplitToningOp.Register(reg);
        ChannelMixerOp.Register(reg);
        ColorNoiseReductionOp.Register(reg);
        LumaNoiseReductionOp.Register(reg);
        DefringeOp.Register(reg);
        DehazeOp.Register(reg);
        FilmicOp.Register(reg);
        ParametricCurveOp.Register(reg);
        OrientationOp.Register(reg);
        CropOp.Register(reg);
        PerspectiveOp.Register(reg);
        WhiteBalanceKelvinOp.Register(reg);
        LutCubeOp.Register(reg);
        SelectiveColorOp.Register(reg);
        ColorUnifyOp.Register(reg);
        BlackWhiteOp.Register(reg);
        InvertOp.Register(reg);
        ChannelGainOp.Register(reg);
        AiDenoiseOp.Register(reg);
        HealingOp.Register(reg);
        LensCorrectionOp.Register(reg);
        // Masked op cần chính registry để dựng inner op -> đăng ký bằng closure.
        reg.Register(MaskedOp.Type, p => MaskedOp.FromParams(p, reg));
        return reg;
    }
}
