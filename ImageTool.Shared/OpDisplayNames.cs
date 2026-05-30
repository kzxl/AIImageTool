using System.Collections.Generic;

namespace ImageTool.Shared;

/// <summary>
/// Ánh xạ OpType kỹ thuật -> nhãn hiển thị thân thiện cho UI (history panel, status bar undo/redo).
/// Tập trung 1 chỗ để nhãn nhất quán giữa các nơi (11.11). Thuần tra cứu -> unit test được.
/// </summary>
public static class OpDisplayNames
{
    private static readonly Dictionary<string, string> Map = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["DevelopBasic"] = "Basic",
        ["ToneCurve"] = "Tone Curve",
        ["ParametricCurve"] = "Parametric Curve",
        ["HslMixer"] = "HSL / Color Mixer",
        ["Clarity"] = "Clarity",
        ["Texture"] = "Texture",
        ["Sharpen"] = "Sharpen",
        ["Dehaze"] = "Dehaze",
        ["Filmic"] = "Filmic",
        ["SplitToning"] = "Split Toning",
        ["ChannelMixer"] = "Channel Mixer",
        ["ColorGrading"] = "Color Grading",
        ["Vignette"] = "Vignette",
        ["Grain"] = "Grain",
        ["ColorNoiseReduction"] = "Color NR",
        ["LumaNoiseReduction"] = "Luminance NR",
        ["Defringe"] = "Defringe",
        ["Orientation"] = "Rotate / Flip",
        ["Crop"] = "Crop / Straighten",
        ["Perspective"] = "Perspective",
        ["Liquify"] = "Liquify / Warp",
        ["FilmNegative"] = "Film Negative",
        ["WBKelvin"] = "White Balance (K)",
        ["SelectiveColor"] = "Selective Color",
        ["ColorUnify"] = "Color Unify",
        ["LutCube"] = "3D LUT",
        ["Masked"] = "Local Adjustment",
    };

    /// <summary>
    /// Nhãn hiển thị cho 1 op. Ưu tiên <paramref name="title"/> nếu có; nếu không, tra map theo
    /// <paramref name="opType"/>; cuối cùng fallback về chính opType.
    /// </summary>
    public static string Get(string opType, string? title = null)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title!;
        return Map.TryGetValue(opType, out var name) ? name : opType;
    }
}
