using System.Collections.Generic;

namespace ImageTool.Shared;

/// <summary>
/// Preset xuất cho mạng xã hội (#10) — kích thước px chuẩn của từng nền tảng. Export crop-to-fill
/// về đúng tỉ lệ (center crop, không méo) rồi resize đúng px. Dùng cho nút "Social Export".
/// </summary>
public static class SocialPresets
{
    public sealed class Item
    {
        public string Name { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public string Format { get; init; } = "jpg";
        public int Quality { get; init; } = 90;
    }

    public static readonly Item[] All =
    {
        new() { Name = "Instagram Square (1080×1080)",     Width = 1080, Height = 1080 },
        new() { Name = "Instagram Portrait (1080×1350)",   Width = 1080, Height = 1350 },
        new() { Name = "Instagram Story (1080×1920)",      Width = 1080, Height = 1920 },
        new() { Name = "Instagram Landscape (1080×566)",   Width = 1080, Height = 566 },
        new() { Name = "Facebook Post (1200×630)",         Width = 1200, Height = 630 },
        new() { Name = "Facebook Cover (820×312)",         Width = 820,  Height = 312 },
        new() { Name = "Twitter/X Post (1600×900)",        Width = 1600, Height = 900 },
        new() { Name = "YouTube Thumbnail (1280×720)",     Width = 1280, Height = 720 },
        new() { Name = "TikTok / Reels (1080×1920)",       Width = 1080, Height = 1920 },
        new() { Name = "LinkedIn Post (1200×627)",         Width = 1200, Height = 627 },
        new() { Name = "Pinterest Pin (1000×1500)",        Width = 1000, Height = 1500 },
    };

    /// <summary>Tham số BatchJob cho 1 preset social (crop-to-fill + resize đúng px + jpg quality).</summary>
    public static Dictionary<string, string> ToJobParams(Item it, string outDir, string pattern)
        => new()
        {
            ["format"] = it.Format,
            ["quality"] = it.Quality.ToString(),
            ["exactWidth"] = it.Width.ToString(),
            ["exactHeight"] = it.Height.ToString(),
            ["outDir"] = outDir,
            ["pattern"] = pattern,
            ["outputSharpen"] = "screen",   // bù mềm khi resize cho màn hình
            ["copyExif"] = "false",         // social thường strip metadata
            ["stripMetadata"] = "true",
            ["outputProfile"] = "srgb",     // sRGB an toàn cho web
        };
}
