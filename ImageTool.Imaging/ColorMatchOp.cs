using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Color Match op (#8) — áp tông màu của 1 ảnh tham chiếu (đã đo thống kê Lab) lên ảnh hiện tại.
/// Lưu mean/std Lab của ảnh tham chiếu trong params nên replay được mà không cần giữ ảnh tham chiếu.
/// Strength 0..1 pha trộn. UI: nút "Match Colors..." chọn ảnh tham chiếu -> đo stats -> dựng op.
/// </summary>
public sealed class ColorMatchOp : IEditOp
{
    public const string Type = "ColorMatch";
    public string OpType => Type;

    public float ML, Ma, Mb, SL = -1f, Sa, Sb;  // SL < 0 = chưa cấu hình
    public float Strength;

    public bool IsIdentity => Strength <= 0f || SL < 0f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        var stats = new ColorMatch.Stats(ML, Ma, Mb, SL, Sa, Sb);
        ColorMatch.ApplyStats(image, stats, Strength);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mL"] = F(ML), ["ma"] = F(Ma), ["mb"] = F(Mb),
        ["sL"] = F(SL), ["sa"] = F(Sa), ["sb"] = F(Sb),
        ["strength"] = F(Strength),
    };

    public static ColorMatchOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        ML = EditOpRegistry.F(p, "mL"), Ma = EditOpRegistry.F(p, "ma"), Mb = EditOpRegistry.F(p, "mb"),
        SL = EditOpRegistry.F(p, "sL", -1f), Sa = EditOpRegistry.F(p, "sa"), Sb = EditOpRegistry.F(p, "sb"),
        Strength = EditOpRegistry.F(p, "strength"),
    };

    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}
