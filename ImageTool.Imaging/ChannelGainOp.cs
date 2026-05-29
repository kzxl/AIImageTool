using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Nhân gain per-channel RGB trong linear light (13.2 apply). Là cách áp kết quả Auto White
/// Balance (gray-world/white-patch) hoặc gain trắng tuỳ ý vào pipeline phi phá hủy. Đơn giản,
/// thuần tham số, replay được.
/// </summary>
public sealed class ChannelGainOp : IEditOp
{
    public const string Type = "ChannelGain";
    public string OpType => Type;

    public float R = 1f, G = 1f, B = 1f;

    public bool IsIdentity =>
        MathF.Abs(R - 1f) < 1e-4f && MathF.Abs(G - 1f) < 1e-4f && MathF.Abs(B - 1f) < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float r = R, g = G, b = B;
        image.ProcessPixels((ref float pr, ref float pg, ref float pb, ref float pa) =>
        {
            pr *= r; pg *= g; pb *= b;
            if (pr < 0f) pr = 0f; if (pg < 0f) pg = 0f; if (pb < 0f) pb = 0f;
        });
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["r"] = F(R), ["g"] = F(G), ["b"] = F(B),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static ChannelGainOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        R = EditOpRegistry.F(p, "r", 1f),
        G = EditOpRegistry.F(p, "g", 1f),
        B = EditOpRegistry.F(p, "b", 1f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
