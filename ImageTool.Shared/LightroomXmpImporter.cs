using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Import preset Lightroom (.xmp) — map các thuộc tính Camera Raw Settings (namespace crs:) sang
/// chuỗi <see cref="EditOperation"/> của pipeline này (9.3). Hỗ trợ các trường phổ biến nhất:
/// Exposure/Contrast/Highlights/Shadows/Whites/Blacks/Vibrance/Saturation/Temperature/Tint
/// (cả biến thể "2012") + Clarity/Dehaze/Sharpness/Vignette + chuyển B&amp;W.
///
/// LR dùng thang riêng (vd Exposure2012 theo EV; Contrast2012 -100..100). Helper quy về thang
/// nội bộ [-1..1] / EV. Thuần parse XML + map -> unit test trực tiếp (không đụng file).
/// </summary>
public static class LightroomXmpImporter
{
    private static readonly XNamespace Crs = "http://ns.adobe.com/camera-raw-settings/1.0/";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>Parse nội dung XMP -> danh sách EditOperation (PluginId="Develop"). Rỗng nếu không có gì.</summary>
    public static List<EditOperation> Parse(string xmpContent)
    {
        var ops = new List<EditOperation>();
        if (string.IsNullOrWhiteSpace(xmpContent)) return ops;

        Dictionary<string, string> crs;
        try { crs = ExtractCrs(xmpContent); }
        catch (Exception ex) { AppLog.Warn("LrXmp.Parse", ex.Message); return ops; }
        // Không bail khi crs rỗng: tone curve nằm trong element lồng nhau (ExtractCrs bỏ qua),
        // vẫn cần parse riêng. Chỉ các op dựa trên crs flat mới phụ thuộc dict này.

        // --- DevelopBasic ---
        var basic = new Dictionary<string, string>();
        // Exposure (EV) — ưu tiên 2012.
        AddIf(basic, "exposure", FirstNum(crs, "Exposure2012", "Exposure"));
        // Các trường thang -100..100 -> [-1..1].
        AddScaled(basic, "contrast", crs, 100, "Contrast2012", "Contrast");
        AddScaled(basic, "highlights", crs, 100, "Highlights2012", "HighlightRecovery");
        AddScaled(basic, "shadows", crs, 100, "Shadows2012", "FillLight");
        AddScaled(basic, "whites", crs, 100, "Whites2012");
        AddScaled(basic, "blacks", crs, 100, "Blacks2012", "Blacks");
        AddScaled(basic, "vibrance", crs, 100, "Vibrance");
        AddScaled(basic, "saturation", crs, 100, "Saturation");
        // Temperature/Tint: LR Temperature là Kelvin (cho RAW) hoặc -100..100 (non-RAW). Chỉ map khi nhỏ.
        AddTempTint(basic, crs);

        if (basic.Count > 0)
            ops.Add(Op(DevelopBasicOp_Type, "Basic (LR)", basic));

        // --- Clarity / Dehaze (Presence) ---
        var clarity = ScaledVal(crs, 100, "Clarity2012", "Clarity");
        if (clarity.HasValue && MathAbs(clarity.Value) > 1e-4)
            ops.Add(Op("Clarity", "Clarity (LR)", new() { ["amount"] = Fmt(clarity.Value) }));
        var dehaze = ScaledVal(crs, 100, "Dehaze", "DehazeAmount");
        if (dehaze.HasValue && MathAbs(dehaze.Value) > 1e-4)
            ops.Add(Op("Dehaze", "Dehaze (LR)", new() { ["amount"] = Fmt(dehaze.Value) }));

        // --- Vignette (post-crop) ---
        var vig = ScaledVal(crs, 100, "PostCropVignetteAmount");
        if (vig.HasValue && MathAbs(vig.Value) > 1e-4)
            ops.Add(Op("Vignette", "Vignette (LR)", new() { ["amount"] = Fmt(vig.Value) }));

        // --- Sharpen ---
        var sharp = ScaledVal(crs, 150, "Sharpness");
        if (sharp.HasValue && sharp.Value > 1e-4)
            ops.Add(Op("Sharpen", "Sharpen (LR)", new() { ["amount"] = Fmt(Math.Clamp(sharp.Value, 0, 1)) }));

        // --- B&W ---
        if (crs.TryGetValue("ConvertToGrayscale", out var bw) && bw.Trim().Equals("True", StringComparison.OrdinalIgnoreCase))
            ops.Add(Op("BlackWhite", "B&W (LR)", new() { ["enabled"] = "true" }));

        // --- Split Toning (hue 0..360 giữ nguyên, sat 0..100 -> 0..1, balance -100..100 -> -1..1) ---
        var split = new Dictionary<string, string>();
        if (TryNum(crs, "SplitToningHighlightHue", out var hh)) split["hiHue"] = Fmt(hh);
        if (TryNum(crs, "SplitToningHighlightSaturation", out var hs)) split["hiSat"] = Fmt(hs / 100.0);
        if (TryNum(crs, "SplitToningShadowHue", out var sh2)) split["shHue"] = Fmt(sh2);
        if (TryNum(crs, "SplitToningShadowSaturation", out var ss)) split["shSat"] = Fmt(ss / 100.0);
        if (TryNum(crs, "SplitToningBalance", out var sbal)) split["balance"] = Fmt(sbal / 100.0);
        // chỉ thêm op khi có saturation (nếu không split toning vô hiệu).
        bool hasSplit = (split.TryGetValue("hiSat", out var hsv) && double.Parse(hsv, CultureInfo.InvariantCulture) > 1e-4)
                     || (split.TryGetValue("shSat", out var ssv) && double.Parse(ssv, CultureInfo.InvariantCulture) > 1e-4);
        if (hasSplit)
            ops.Add(Op("SplitToning", "Split Toning (LR)", split));

        // --- Tone Curve (point list, 0..255 -> 0..1): tổng + per-channel R/G/B ---
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xmpContent);
            string? rgb = ParseToneCurve(doc, "ToneCurvePV2012") ?? ParseToneCurve(doc, "ToneCurve");
            string? cr = ParseToneCurve(doc, "ToneCurvePV2012Red");
            string? cg = ParseToneCurve(doc, "ToneCurvePV2012Green");
            string? cb = ParseToneCurve(doc, "ToneCurvePV2012Blue");
            if (rgb != null || cr != null || cg != null || cb != null)
            {
                var cp = new Dictionary<string, string>();
                if (rgb != null) cp["rgb"] = rgb;
                if (cr != null) cp["r"] = cr;
                if (cg != null) cp["g"] = cg;
                if (cb != null) cp["b"] = cb;
                ops.Add(Op("ToneCurve", "Tone Curve (LR)", cp));
            }
        }
        catch (Exception ex) { AppLog.Warn("LrXmp.ToneCurve", ex.Message); }

        return ops;
    }

    /// <summary>
    /// Trích 1 đường cong từ element crs có tên <paramref name="localName"/> — rdf:Seq các "x, y" trong
    /// thang 0..255. Chuẩn hoá về 0..1, serialize "x,y;x,y". Trả null nếu không có / là identity.
    /// </summary>
    private static string? ParseToneCurve(XDocument doc, string localName)
    {
        var el = FindCrsElement(doc, localName);
        if (el == null) return null;

        var pts = new List<(double X, double Y)>();
        foreach (var li in el.Descendants(Rdf + "li"))
        {
            var parts = li.Value.Split(',');
            if (parts.Length != 2) continue;
            if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                pts.Add((Math.Clamp(x / 255.0, 0, 1), Math.Clamp(y / 255.0, 0, 1)));
        }
        if (pts.Count < 2) return null;
        // Bỏ qua nếu là đường thẳng y=x (identity) -> không tạo op thừa.
        bool identity = pts.TrueForAll(p => MathAbs(p.X - p.Y) < 0.004);
        if (identity) return null;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(Fmt(pts[i].X)).Append(',').Append(Fmt(pts[i].Y));
        }
        return sb.ToString();
    }

    private static XElement? FindCrsElement(XDocument doc, string localName)
    {
        foreach (var el in doc.Descendants(Crs + localName))
            return el;
        return null;
    }

    // map Temperature/Tint sang temp/tint [-1..1] của DevelopBasic (chỉ khi là thang nhỏ -100..100).
    private static void AddTempTint(Dictionary<string, string> basic, Dictionary<string, string> crs)
    {
        if (TryNum(crs, "Temperature", out var t) && MathAbs(t) <= 100)
            AddIf(basic, "temp", t / 100.0);
        if (TryNum(crs, "Tint", out var tint) && MathAbs(tint) <= 100)
            AddIf(basic, "tint", tint / 100.0);
    }

    private const string DevelopBasicOp_Type = "DevelopBasic";

    private static EditOperation Op(string type, string title, Dictionary<string, string> p)
        => new() { PluginId = "Develop", OpType = type, Title = title, Params = p };

    /// <summary>Trích mọi thuộc tính/element thuộc namespace crs: thành dict (bỏ tiền tố).</summary>
    private static Dictionary<string, string> ExtractCrs(string xmp)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = XDocument.Parse(xmp);

        foreach (var desc in doc.Descendants(Rdf + "Description"))
        {
            // dạng attribute: crs:Exposure2012="..."
            foreach (var attr in desc.Attributes())
                if (attr.Name.Namespace == Crs)
                    dict[attr.Name.LocalName] = attr.Value;

            // dạng element con: <crs:Exposure2012>...</crs:Exposure2012>
            foreach (var el in desc.Elements())
                if (el.Name.Namespace == Crs && !el.HasElements)
                    dict[el.Name.LocalName] = el.Value;
        }
        return dict;
    }

    private static void AddIf(Dictionary<string, string> d, string key, double? v)
    {
        if (v.HasValue && MathAbs(v.Value) > 1e-6) d[key] = Fmt(v.Value);
    }

    private static void AddScaled(Dictionary<string, string> d, string key, Dictionary<string, string> crs, double scale, params string[] crsKeys)
    {
        var v = ScaledVal(crs, scale, crsKeys);
        if (v.HasValue && MathAbs(v.Value) > 1e-4) d[key] = Fmt(v.Value);
    }

    private static double? ScaledVal(Dictionary<string, string> crs, double scale, params string[] keys)
    {
        var raw = FirstNum(crs, keys);
        return raw.HasValue ? raw.Value / scale : (double?)null;
    }

    private static double? FirstNum(Dictionary<string, string> crs, params string[] keys)
    {
        foreach (var k in keys)
            if (TryNum(crs, k, out var v)) return v;
        return null;
    }

    private static bool TryNum(Dictionary<string, string> crs, string key, out double v)
    {
        v = 0;
        return crs.TryGetValue(key, out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static double MathAbs(double v) => v < 0 ? -v : v;
    private static string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
