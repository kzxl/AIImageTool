namespace ImageTool.Imaging;

/// <summary>
/// Preset gradient map dựng sẵn (#5) — tên + màu 3 chặng (hex sRGB Shadow/Mid/Highlight).
/// UI chỉ cần chọn tên + opacity; preset cấp màu. "None" = không áp.
/// </summary>
public static class GradientMapPresets
{
    public sealed class Preset
    {
        public string Name { get; init; } = "";
        public string Shadow { get; init; } = "000000";
        public string Mid { get; init; } = "808080";
        public string High { get; init; } = "FFFFFF";
        public bool IsNone => string.Equals(Name, "None", System.StringComparison.OrdinalIgnoreCase);
    }

    public static readonly Preset[] All =
    {
        new() { Name = "None" },
        new() { Name = "Sepia",          Shadow = "1A0F00", Mid = "8A5A2B", High = "FFE9C7" },
        new() { Name = "Cyanotype",      Shadow = "06141F", Mid = "1C5C7A", High = "D6EEF5" },
        new() { Name = "Teal–Orange",    Shadow = "07313B", Mid = "5E7E82", High = "FFB066" },
        new() { Name = "Noir (B&W)",     Shadow = "000000", Mid = "7F7F7F", High = "FFFFFF" },
        new() { Name = "Gold–Teal",      Shadow = "0E2A2E", Mid = "8C7A4B", High = "FFE7A8" },
        new() { Name = "Purple Haze",    Shadow = "140A1F", Mid = "6A4C8C", High = "F2D9FF" },
        new() { Name = "Forest",         Shadow = "06140A", Mid = "3E6B3A", High = "E6F2C7" },
        new() { Name = "Blush",          Shadow = "200A10", Mid = "9C5E66", High = "FFE3E0" },
        new() { Name = "Bleach Bypass",  Shadow = "1A1A18", Mid = "8C8A80", High = "FCFBF4" },
    };

    public static Preset ByName(string? name)
    {
        if (!string.IsNullOrEmpty(name))
            foreach (var p in All)
                if (string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase)) return p;
        return All[0];
    }

    public static Preset ByIndex(int i) => (i >= 0 && i < All.Length) ? All[i] : All[0];
}
