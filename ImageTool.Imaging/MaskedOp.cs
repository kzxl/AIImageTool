using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Bọc 1 op nội (inner) + 1 mask, chỉ áp hiệu ứng theo trọng số mask (local adjustment).
/// Cách làm: clone ảnh, áp inner op lên bản clone (full), rồi blend về ảnh gốc theo mask:
///   out = orig*(1-m) + edited*m.
/// Nhờ vậy bất kỳ op global nào (DevelopBasic, Clarity, Sharpen...) đều thành local adjustment.
///
/// Serialize: OpType="Masked"; Params chứa "inner" = OpType nội, "mask" = loại mask,
/// cùng toàn bộ params của inner và mask (đã trộn). Để tránh đụng key, inner params giữ
/// nguyên, mask params có tiền tố "mask" cho loại.
/// </summary>
public sealed class MaskedOp : IEditOp
{
    public const string Type = "Masked";
    public string OpType => Type;

    private readonly IEditOp _inner;
    private readonly IMaskGenerator? _mask;
    private readonly LuminanceRangeMask? _rangeMask; // range mask cần pixel
    private readonly ColorRangeMask? _colorMask;     // color range mask cần pixel
    private readonly IReadOnlyDictionary<string, string> _params;

    public MaskedOp(IEditOp inner, IMaskGenerator? mask, LuminanceRangeMask? rangeMask, IReadOnlyDictionary<string, string> rawParams)
        : this(inner, mask, rangeMask, null, rawParams) { }

    public MaskedOp(IEditOp inner, IMaskGenerator? mask, LuminanceRangeMask? rangeMask, ColorRangeMask? colorMask, IReadOnlyDictionary<string, string> rawParams)
    {
        _inner = inner;
        _mask = mask;
        _rangeMask = rangeMask;
        _colorMask = colorMask;
        _params = rawParams;
    }

    public void Apply(LinearImage image, float scale)
    {
        // Sinh mask.
        float[] m;
        if (_rangeMask != null) m = _rangeMask.GenerateFrom(image);
        else if (_colorMask != null) m = _colorMask.GenerateFrom(image);
        else if (_mask != null) m = _mask.Generate(image.Width, image.Height);
        else return;

        // Nếu vừa có hình học vừa có range, nhân chúng (đã gộp ở builder nếu cần). Ở đây 1 mask.

        // Clone, áp inner op lên bản clone.
        var edited = image.Clone();
        _inner.Apply(edited, scale);

        // Blend theo mask.
        float[] o = image.Pixels;
        float[] e = edited.Pixels;
        int n = image.PixelCount;
        Parallel.For(0, n, i =>
        {
            float w = m[i];
            if (w <= 0f) return;
            int p = i * 4;
            o[p] = o[p] + (e[p] - o[p]) * w;
            o[p + 1] = o[p + 1] + (e[p + 1] - o[p + 1]) * w;
            o[p + 2] = o[p + 2] + (e[p + 2] - o[p + 2]) * w;
            // alpha giữ nguyên
        });
    }

    public Dictionary<string, string> ToParams()
    {
        var p = new Dictionary<string, string>(_params)
        {
            ["inner"] = _inner.OpType
        };
        return p;
    }

    /// <summary>
    /// Dựng MaskedOp từ params. Cần registry để dựng inner op.
    /// "inner" = OpType nội; "mask" = loại mask. Inner và mask params nằm chung dict.
    /// </summary>
    public static MaskedOp FromParams(IReadOnlyDictionary<string, string> p, EditOpRegistry reg)
    {
        string innerType = EditOpRegistry.S(p, "inner");
        var inner = reg.Create(innerType, p) ?? new NoopOp();

        string maskType = EditOpRegistry.S(p, "mask");
        IMaskGenerator? mask = null;
        LuminanceRangeMask? range = null;
        ColorRangeMask? colorRange = null;
        switch (maskType)
        {
            case LinearGradientMask.Type: mask = LinearGradientMask.FromParams(p); break;
            case RadialMask.Type: mask = RadialMask.FromParams(p); break;
            case LuminanceRangeMask.Type: range = LuminanceRangeMask.FromParams(p); break;
            case ColorRangeMask.Type: colorRange = ColorRangeMask.FromParams(p); break;
            case BrushMask.Type: mask = BrushMask.FromParams(p); break;
            case RasterMask.Type: mask = RasterMask.FromParams(p); break;
        }
        return new MaskedOp(inner, mask, range, colorRange, p);
    }

    private sealed class NoopOp : IEditOp
    {
        public string OpType => "Noop";
        public void Apply(LinearImage image, float scale) { }
    }
}
