using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ImageTool.Shared;

/// <summary>
/// Gợi ý màu để tăng tương phản/hài hoà từ bảng màu chủ đạo (color theory thuần, KHÔNG AI).
/// Tính màu bổ túc/split-complementary/triadic trên vòng tròn HSL của màu chủ đạo, và đánh giá
/// "độ tương phản màu" hiện tại (độ trải hue + chênh lệch luminance). Thuần toán -> test trực tiếp,
/// chạy tức thì, không cần model.
/// </summary>
public static class ColorSuggestion
{
    public sealed class Suggestion
    {
        public byte R { get; init; }
        public byte G { get; init; }
        public byte B { get; init; }
        public string Role { get; init; } = "";   // vai trò: "Bổ túc", "Split A/B", "Triadic"...
        public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    }

    /// <summary>
    /// Từ màu chủ đạo (RGB 0-255) sinh các màu gợi ý hài hoà. Lấy hue của màu chủ đạo, quay trên
    /// vòng tròn để ra complementary (+180°), split (+150/+210°), triadic (+120/+240°),
    /// giữ saturation/lightness vừa phải để dùng làm sắc grade.
    /// </summary>
    public static List<Suggestion> FromDominant(byte r, byte g, byte b)
    {
        RgbToHsl(r / 255f, g / 255f, b / 255f, out float h, out float s, out float l);
        // dùng độ bão hoà tối thiểu để màu gợi ý không bị xám.
        float sat = Math.Max(0.5f, s);
        float lit = Math.Clamp(l, 0.35f, 0.65f);

        var result = new List<Suggestion>();
        void Add(float hueDeg, string role)
        {
            float hh = ((hueDeg % 360f) + 360f) % 360f;
            HslToRgb(hh, sat, lit, out float rr, out float gg, out float bb);
            result.Add(new Suggestion
            {
                R = (byte)Math.Clamp(rr * 255f, 0, 255),
                G = (byte)Math.Clamp(gg * 255f, 0, 255),
                B = (byte)Math.Clamp(bb * 255f, 0, 255),
                Role = role
            });
        }
        Add(h + 180f, "Bổ túc");
        Add(h + 150f, "Split A");
        Add(h + 210f, "Split B");
        Add(h + 120f, "Triadic A");
        Add(h + 240f, "Triadic B");
        return result;
    }

    /// <summary>
    /// Đánh giá độ tương phản màu của 1 bảng swatch (RGB list). Trả điểm 0..1 + nhãn gợi ý:
    /// kết hợp độ trải hue (ảnh đơn sắc -> thấp) và chênh lệch luminance (phẳng -> thấp).
    /// </summary>
    public static (double Score, string Advice) AssessContrast(IReadOnlyList<(byte R, byte G, byte B)> swatches)
    {
        if (swatches == null || swatches.Count < 2)
            return (0, "Chưa đủ màu để đánh giá.");

        // chênh lệch luminance (Rec.709 trên sRGB).
        var lums = swatches.Select(c => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B).ToList();
        double lumSpread = (lums.Max() - lums.Min()) / 255.0; // 0..1

        // độ trải hue: khoảng cách hue lớn nhất giữa các cặp.
        var hues = swatches.Select(c => { RgbToHsl(c.R / 255f, c.G / 255f, c.B / 255f, out var hh, out _, out _); return hh; }).ToList();
        double hueSpread = MaxHueSpread(hues) / 180.0; // 0..1 (180° = đối xứng tối đa)

        double score = Math.Clamp(0.6 * lumSpread + 0.4 * hueSpread, 0, 1);
        string advice = score switch
        {
            < 0.25 => "Ảnh phẳng màu — thử thêm sắc bổ túc vào shadow/highlight để tăng chiều sâu.",
            < 0.5 => "Tương phản màu trung bình — split-toning nhẹ sẽ làm ảnh nổi hơn.",
            < 0.75 => "Tương phản màu tốt.",
            _ => "Tương phản màu mạnh — cân nhắc giảm nếu muốn tông êm dịu."
        };
        return (score, advice);
    }

    private static double MaxHueSpread(List<float> hues)
    {
        double max = 0;
        for (int i = 0; i < hues.Count; i++)
            for (int j = i + 1; j < hues.Count; j++)
            {
                double d = Math.Abs(hues[i] - hues[j]);
                if (d > 180) d = 360 - d;
                if (d > max) max = d;
            }
        return max;
    }

    private static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2f;
        float d = max - min;
        if (d < 1e-6f) { h = 0; s = 0; return; }
        s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
        if (max == r) h = 60f * (((g - b) / d) % 6f);
        else if (max == g) h = 60f * (((b - r) / d) + 2f);
        else h = 60f * (((r - g) / d) + 4f);
        if (h < 0) h += 360f;
    }

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        if (s < 1e-6f) { r = g = b = l; return; }
        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Math.Abs(((h / 60f) % 2f) - 1f));
        float m = l - c / 2f;
        float r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }
        r = r1 + m; g = g1 + m; b = b1 + m;
    }
}
