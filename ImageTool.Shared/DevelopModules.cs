using System;
using System.Collections.Generic;
using System.Linq;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Shared;

/// <summary>
/// Định nghĩa các "module" Develop (gom nhóm OpType theo panel) + thứ tự xử lý chuẩn của pipeline,
/// phục vụ Selective Paste (D6.1 Darktable): copy/paste TỪNG module giữa các ảnh thay vì cả cụm.
///
/// Thuần dữ liệu + hàm tra cứu/merge -> unit test được, không phụ thuộc UI.
/// </summary>
public static class DevelopModules
{
    /// <summary>1 module = 1 nhóm OpType cùng ý nghĩa (khớp nhóm trong DevelopPanel).</summary>
    public sealed class Module
    {
        public string Key { get; }
        public string Label { get; }
        public IReadOnlyList<string> OpTypes { get; }
        public Module(string key, string label, params string[] opTypes)
        { Key = key; Label = label; OpTypes = opTypes; }
    }

    /// <summary>
    /// Thứ tự xử lý CHUẨN của pipeline (khớp DevelopPanel.BuildOps). Op không có trong danh sách
    /// xếp sau cùng (ổn định). Dùng để sắp xếp lại sau khi merge selective paste.
    /// </summary>
    public static readonly string[] PipelineOrder =
    {
        CropOp.Type, OrientationOp.Type, PerspectiveOp.Type, LensCorrectionOp.Type, HealingOp.Type,
        WhiteBalanceKelvinOp.Type, ChannelGainOp.Type,
        DevelopBasicOp.Type,
        ParametricCurveOp.Type, ToneCurveOp.Type,
        DehazeOp.Type, FilmicOp.Type, ToneEqualizerOp.Type, SigmoidOp.Type, FilmicRgbOp.Type, RgbLevelsOp.Type,
        HslMixerOp.Type, ColorBalanceRgbOp.Type, ColorContrastOp.Type, VelviaOp.Type, ChannelMixerOp.Type,
        SplitToningOp.Type, ColorGradingOp.Type, SelectiveColorOp.Type, ColorUnifyOp.Type, LutCubeOp.Type,
        ColorNoiseReductionOp.Type, LumaNoiseReductionOp.Type, HotPixelOp.Type, CaCorrectOp.Type, DefringeOp.Type,
        ClarityOp.Type, TextureOp.Type, SharpenOp.Type,
        VignetteOp.Type, GrainOp.Type,
        BlackWhiteOp.Type, InvertOp.Type, AiDenoiseOp.Type,
        MaskedOp.Type,
        AiUpscaleOp.Type,
    };

    /// <summary>Danh sách module (theo thứ tự hiển thị), gom OpType liên quan.</summary>
    public static readonly IReadOnlyList<Module> All = new List<Module>
    {
        new("geometry", "Geometry", CropOp.Type, OrientationOp.Type, PerspectiveOp.Type, LensCorrectionOp.Type),
        new("healing", "Healing / Clone", HealingOp.Type),
        new("wb", "White Balance", WhiteBalanceKelvinOp.Type, ChannelGainOp.Type),
        new("basic", "Basic Tone", DevelopBasicOp.Type),
        new("curve", "Tone Curve", ParametricCurveOp.Type, ToneCurveOp.Type),
        new("tonemap", "Tone Mapping", DehazeOp.Type, FilmicOp.Type, ToneEqualizerOp.Type, SigmoidOp.Type, FilmicRgbOp.Type, RgbLevelsOp.Type),
        new("colormix", "Color Mixer", HslMixerOp.Type, ColorBalanceRgbOp.Type, ColorContrastOp.Type, VelviaOp.Type, ChannelMixerOp.Type),
        new("colorgrade", "Color Grading", SplitToningOp.Type, ColorGradingOp.Type, SelectiveColorOp.Type, ColorUnifyOp.Type),
        new("lut", "3D LUT", LutCubeOp.Type),
        new("detail", "Detail", ColorNoiseReductionOp.Type, LumaNoiseReductionOp.Type, HotPixelOp.Type, CaCorrectOp.Type, DefringeOp.Type, ClarityOp.Type, TextureOp.Type, SharpenOp.Type),
        new("effects", "Effects", VignetteOp.Type, GrainOp.Type),
        new("bw", "Black & White", BlackWhiteOp.Type),
        new("invert", "Negative / Invert", InvertOp.Type),
        new("ai", "AI (Denoise/Upscale)", AiDenoiseOp.Type, AiUpscaleOp.Type),
        new("local", "Local Adjustments", MaskedOp.Type),
    };

    private static readonly Dictionary<string, int> OrderIndex = BuildOrderIndex();
    private static readonly Dictionary<string, Module> TypeToModule = BuildTypeToModule();

    private static Dictionary<string, int> BuildOrderIndex()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < PipelineOrder.Length; i++) d[PipelineOrder[i]] = i;
        return d;
    }

    private static Dictionary<string, Module> BuildTypeToModule()
    {
        var d = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in All)
            foreach (var t in m.OpTypes)
                d[t] = m;
        return d;
    }

    /// <summary>Chỉ số thứ tự pipeline của 1 OpType (op không biết -> đẩy cuối).</summary>
    public static int CanonicalIndex(string opType)
        => OrderIndex.TryGetValue(opType, out var i) ? i : int.MaxValue;

    /// <summary>Module chứa OpType (null nếu không thuộc module nào).</summary>
    public static Module? ModuleOf(string opType)
        => TypeToModule.TryGetValue(opType, out var m) ? m : null;

    /// <summary>Khoá module chứa OpType ("" nếu không thuộc).</summary>
    public static string ModuleKeyOf(string opType) => ModuleOf(opType)?.Key ?? "";

    /// <summary>Những module có ít nhất 1 op trong danh sách (để hiển thị "có gì để dán").</summary>
    public static IReadOnlyList<Module> ModulesPresent(IEnumerable<EditOperation> ops)
    {
        var keys = new HashSet<string>(ops.Select(o => ModuleKeyOf(o.OpType)).Where(k => k.Length > 0), StringComparer.OrdinalIgnoreCase);
        return All.Where(m => keys.Contains(m.Key)).ToList();
    }

    /// <summary>
    /// Merge selective paste: lấy <paramref name="targetOps"/> hiện có, với mỗi module được CHỌN thì
    /// thay toàn bộ op của module đó bằng op tương ứng từ <paramref name="sourceOps"/>; module không chọn
    /// giữ nguyên op đích. Op của module được chọn nhưng nguồn không có -> bị gỡ (reset module đó).
    /// Kết quả sắp xếp lại theo <see cref="PipelineOrder"/> (ổn định trong cùng OpType).
    /// </summary>
    public static List<EditOperation> SelectivePaste(
        IReadOnlyList<EditOperation> targetOps,
        IReadOnlyList<EditOperation> sourceOps,
        ISet<string> selectedModuleKeys)
    {
        var result = new List<EditOperation>();

        // 1) Giữ op đích của module KHÔNG được chọn.
        foreach (var op in targetOps)
        {
            string key = ModuleKeyOf(op.OpType);
            if (!selectedModuleKeys.Contains(key))
                result.Add(op);
        }

        // 2) Thêm op nguồn của module ĐƯỢC chọn.
        foreach (var op in sourceOps)
        {
            string key = ModuleKeyOf(op.OpType);
            if (selectedModuleKeys.Contains(key))
                result.Add(op);
        }

        // 3) Sắp xếp lại theo thứ tự pipeline chuẩn (stable theo OpType giữ thứ tự nội bộ).
        return SortCanonical(result);
    }

    /// <summary>Sắp xếp 1 danh sách op theo thứ tự pipeline chuẩn (ổn định cho op cùng OpType).</summary>
    public static List<EditOperation> SortCanonical(IEnumerable<EditOperation> ops)
        => ops.OrderBy(o => CanonicalIndex(o.OpType)).ToList();
}
