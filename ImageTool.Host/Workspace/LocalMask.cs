using System;
using System.Collections.Generic;
using System.Globalization;
using ImageTool.Core;
using ImageTool.Imaging;

namespace ImageTool.Host.Workspace;

/// <summary>
/// 1 "local adjustment" kiểu Lightroom: 1 mask (gradient/radial/brush/range) + 1 BỘ ĐẦY ĐỦ
/// slider Light/Color áp cục bộ theo mask (6.7). Mỗi mask sinh ra 1..n <see cref="EditOperation"/>
/// loại <see cref="MaskedOp"/> — mỗi inner op (DevelopBasic / Clarity / Sharpen) 1 MaskedOp,
/// dùng chung tham số mask nên blend cùng vùng.
///
/// Round-trip: BuildOps gộp mask + inner params; LoadFor nhóm các MaskedOp theo "chữ ký mask"
/// để dựng lại danh sách mask.
/// </summary>
public sealed class LocalMask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public string Name { get; set; } = "Mask";

    /// <summary>Loại mask: LinearGradient / Radial / Brush / LumRange / ColorRange.</summary>
    public string MaskType { get; set; } = LinearGradientMask.Type;

    /// <summary>Tham số hình học của mask (toạ độ chuẩn hoá...). Không gồm "mask"/"inner".</summary>
    public Dictionary<string, string> MaskParams { get; set; } = new();

    // --- Bộ chỉnh đầy đủ (6.7) ---
    public float Exposure, Contrast, Highlights, Shadows, Whites, Blacks;
    public float Temp, Tint, Saturation, Vibrance;
    public float Clarity, Sharpen;

    public bool HasAdjustments =>
        !Z(Exposure) || !Z(Contrast) || !Z(Highlights) || !Z(Shadows) || !Z(Whites) || !Z(Blacks) ||
        !Z(Temp) || !Z(Tint) || !Z(Saturation) || !Z(Vibrance) || !Z(Clarity) || !Z(Sharpen);

    private static bool Z(float v) => MathF.Abs(v) < 1e-4f;

    /// <summary>Chữ ký ổn định của mask (type + tham số hình học sắp xếp) để gom nhóm khi load.</summary>
    public string Signature()
    {
        var keys = new List<string>(MaskParams.Keys);
        keys.Sort(StringComparer.Ordinal);
        var sb = new System.Text.StringBuilder(MaskType);
        foreach (var k in keys) sb.Append('|').Append(k).Append('=').Append(MaskParams[k]);
        return sb.ToString();
    }

    /// <summary>Gộp tham số mask vào 1 dict mới (kèm "mask"=type).</summary>
    private Dictionary<string, string> MaskParamBag()
    {
        var d = new Dictionary<string, string>(MaskParams) { ["mask"] = MaskType };
        return d;
    }

    /// <summary>Sinh các EditOperation (MaskedOp) cho mask này. Rỗng nếu không có chỉnh.</summary>
    public List<EditOperation> ToOperations()
    {
        var result = new List<EditOperation>();
        if (!HasAdjustments) return result;

        // 1) DevelopBasic (Light + Color cơ bản)
        var basic = new DevelopBasicOp
        {
            Exposure = Exposure, Contrast = Contrast, Highlights = Highlights, Shadows = Shadows,
            Whites = Whites, Blacks = Blacks, Temp = Temp, Tint = Tint, Saturation = Saturation, Vibrance = Vibrance,
        };
        if (!basic.IsIdentity)
            result.Add(MakeMasked(DevelopBasicOp.Type, basic.ToParams()));

        // 2) Clarity
        var clarity = new ClarityOp { Amount = Clarity };
        if (!clarity.IsIdentity)
            result.Add(MakeMasked(ClarityOp.Type, clarity.ToParams()));

        // 3) Sharpen
        var sharpen = new SharpenOp { Amount = Sharpen };
        if (!sharpen.IsIdentity)
            result.Add(MakeMasked(SharpenOp.Type, sharpen.ToParams()));

        return result;
    }

    private EditOperation MakeMasked(string innerType, Dictionary<string, string> innerParams)
    {
        var p = MaskParamBag();
        foreach (var kv in innerParams) p[kv.Key] = kv.Value;
        p["inner"] = innerType;
        p["maskId"] = Id; // giúp gom nhóm chính xác khi load
        return new EditOperation { PluginId = "Develop", OpType = MaskedOp.Type, Title = $"Local: {Name}", Params = p };
    }

    /// <summary>Nạp 1 giá trị slider cục bộ theo key (dùng khi reconstruct từ inner op params).</summary>
    public void ApplyInner(string innerType, IReadOnlyDictionary<string, string> p)
    {
        switch (innerType)
        {
            case DevelopBasicOp.Type:
                Exposure = F(p, "exposure"); Contrast = F(p, "contrast");
                Highlights = F(p, "highlights"); Shadows = F(p, "shadows");
                Whites = F(p, "whites"); Blacks = F(p, "blacks");
                Temp = F(p, "temp"); Tint = F(p, "tint");
                Saturation = F(p, "saturation"); Vibrance = F(p, "vibrance");
                break;
            case ClarityOp.Type: Clarity = F(p, "amount"); break;
            case SharpenOp.Type: Sharpen = F(p, "amount"); break;
        }
    }

    private static float F(IReadOnlyDictionary<string, string> p, string key)
        => p.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

    /// <summary>Tạo mask mặc định theo loại (hình học hợp lý ở giữa khung).</summary>
    public static LocalMask CreateDefault(string maskType)
    {
        var m = new LocalMask { MaskType = maskType };
        switch (maskType)
        {
            case LinearGradientMask.Type:
                m.Name = "Gradient";
                m.MaskParams = new() { ["x0"] = "0.5", ["y0"] = "0", ["x1"] = "0.5", ["y1"] = "0.5", ["invert"] = "false" };
                break;
            case RadialMask.Type:
                m.Name = "Radial";
                m.MaskParams = new() { ["cx"] = "0.5", ["cy"] = "0.5", ["rx"] = "0.3", ["ry"] = "0.3", ["feather"] = "0.4", ["invert"] = "true" };
                break;
            case BrushMask.Type:
                m.Name = "Brush";
                m.MaskParams = new() { ["radius"] = "0.05", ["hardness"] = "0.5", ["pts"] = "" };
                break;
            case LuminanceRangeMask.Type:
                m.Name = "Luminance Range";
                m.MaskParams = new() { ["min"] = "0", ["max"] = "1", ["smooth"] = "0.1" };
                break;
            case ColorRangeMask.Type:
                m.Name = "Color Range";
                m.MaskParams = new() { ["hue"] = "0", ["range"] = "30", ["minSat"] = "0.1", ["smooth"] = "0.2" };
                break;
        }
        return m;
    }
}
