using System;
using System.Text;

namespace ImageTool.Imaging;

/// <summary>
/// Đọc ICC profile nhúng (D2.2/7.3) — trích MÔ TẢ profile (vd "Adobe RGB (1998)", "Display P3",
/// "sRGB IEC61966-2.1") để TỰ NHẬN DIỆN gamut nguồn, rồi gợi ý Input Profile đúng cho pipeline.
///
/// ICC binary: header 128 byte (big-endian) + bảng tag. Tag "desc" chứa tên profile, 2 dạng:
///   - v2: textDescriptionType ('desc') — ASCII.
///   - v4: multiLocalizedUnicodeType ('mluc') — UTF-16BE.
/// Thuần parse byte -> unit test trực tiếp (không cần ImageSharp/file). Không áp gì lên pixel.
/// </summary>
public static class IccProfileReader
{
    /// <summary>Trích mô tả profile từ ICC bytes. Trả null nếu không phải ICC hợp lệ / không có 'desc'.</summary>
    public static string? TryReadDescription(byte[]? icc)
    {
        if (icc == null || icc.Length < 132) return null;
        // 'acsp' ở offset 36 xác nhận đây là ICC profile.
        if (icc[36] != (byte)'a' || icc[37] != (byte)'c' || icc[38] != (byte)'s' || icc[39] != (byte)'p')
            return null;

        int tagCount = ReadU32(icc, 128);
        if (tagCount <= 0 || tagCount > 4096) return null;
        int tableStart = 132;
        if (tableStart + tagCount * 12 > icc.Length) return null;

        for (int i = 0; i < tagCount; i++)
        {
            int e = tableStart + i * 12;
            string sig = Ascii(icc, e, 4);
            if (sig != "desc") continue;
            int off = ReadU32(icc, e + 4);
            int size = ReadU32(icc, e + 8);
            if (off <= 0 || size <= 8 || (long)off + size > icc.Length) return null;
            return ParseDescTag(icc, off, size);
        }
        return null;
    }

    private static string? ParseDescTag(byte[] b, int off, int size)
    {
        string type = Ascii(b, off, 4);
        if (type == "desc")
        {
            // textDescriptionType: 4 type + 4 reserved + 4 ASCII count + ASCII string (NUL-terminated).
            int count = ReadU32(b, off + 8);
            int strStart = off + 12;
            if (count <= 0 || strStart + count > b.Length) return null;
            int len = count;
            // bỏ NUL cuối.
            while (len > 0 && b[strStart + len - 1] == 0) len--;
            return len > 0 ? Encoding.ASCII.GetString(b, strStart, len) : null;
        }
        if (type == "mluc")
        {
            // multiLocalizedUnicodeType: 4 type + 4 reserved + 4 numRecords + 4 recordSize
            // + records[ 2 lang, 2 country, 4 length, 4 offset(từ đầu tag) ] + chuỗi UTF-16BE.
            int numRecords = ReadU32(b, off + 8);
            int recordSize = ReadU32(b, off + 12);
            if (numRecords <= 0 || recordSize < 12) return null;
            int recStart = off + 16;
            if (recStart + recordSize > b.Length) return null;
            int length = ReadU32(b, recStart + 4);   // byte length của chuỗi
            int strOff = ReadU32(b, recStart + 8);    // offset từ đầu tag
            int abs = off + strOff;
            if (length <= 0 || abs + length > b.Length) return null;
            // UTF-16 big-endian.
            var s = Encoding.BigEndianUnicode.GetString(b, abs, length);
            return s.TrimEnd('\0');
        }
        return null;
    }

    /// <summary>
    /// Đoán không gian màu working từ mô tả ICC. Trả null nếu không khớp gamut quen thuộc (giữ nguyên,
    /// để người dùng tự chọn). Khớp không phân biệt hoa thường, theo từ khoá đặc trưng.
    /// </summary>
    public static ColorSpaces.Space? GuessSpace(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        string d = description.ToLowerInvariant();

        if (d.Contains("adobe rgb") || d.Contains("adobergb") || d.Contains("adobe (1998)"))
            return ColorSpaces.Space.AdobeRgb;
        if (d.Contains("display p3") || d.Contains("displayp3") || d.Contains("dci-p3") || d.Contains("dci p3"))
            return ColorSpaces.Space.DisplayP3;
        if (d.Contains("2020") || d.Contains("rec2020") || d.Contains("rec. 2020") || d.Contains("bt.2020"))
            return ColorSpaces.Space.Rec2020;
        if (d.Contains("srgb"))
            return ColorSpaces.Space.Srgb;
        return null;
    }

    /// <summary>
    /// Đọc ma trận RGB(linear)->XYZ THẬT từ colorant tag của ICC (D2.2/7.3): rXYZ/gXYZ/bXYZ
    /// (mỗi tag 'XYZ ' chứa 3 số s15Fixed16 = giá trị *65536). 3 cột ghép thành ma trận, đã quy
    /// về D65 (PCS ICC là D50 -> Bradford adapt). Trả null nếu thiếu tag / không phải matrix profile.
    /// Đây là cách nhận diện gamut CHÍNH XÁC, không phụ thuộc tên mô tả.
    /// </summary>
    public static float[]? TryReadRgbToXyzD65(byte[]? icc)
    {
        if (icc == null || icc.Length < 132) return null;
        if (icc[36] != (byte)'a' || icc[37] != (byte)'c' || icc[38] != (byte)'s' || icc[39] != (byte)'p')
            return null;
        int tagCount = ReadU32(icc, 128);
        if (tagCount <= 0 || tagCount > 4096) return null;
        int tableStart = 132;
        if (tableStart + tagCount * 12 > icc.Length) return null;

        float[]? r = null, g = null, b = null;
        for (int i = 0; i < tagCount; i++)
        {
            int e = tableStart + i * 12;
            string sig = Ascii(icc, e, 4);
            if (sig != "rXYZ" && sig != "gXYZ" && sig != "bXYZ") continue;
            int off = ReadU32(icc, e + 4);
            int size = ReadU32(icc, e + 8);
            if (off <= 0 || size < 20 || (long)off + 20 > icc.Length) continue;
            var xyz = ParseXyzTag(icc, off);
            if (xyz == null) continue;
            if (sig == "rXYZ") r = xyz;
            else if (sig == "gXYZ") g = xyz;
            else b = xyz;
        }
        if (r == null || g == null || b == null) return null;

        // Cột r/g/b -> ma trận RGB->XYZ (D50), row-major.
        float[] d50 =
        {
            r[0], g[0], b[0],
            r[1], g[1], b[1],
            r[2], g[2], b[2],
        };
        return ColorSpaces.AdaptD50ToD65(d50);
    }

    /// <summary>Parse 1 tag 'XYZ ' (type(4)+reserved(4)+ 3*s15Fixed16). Trả [X,Y,Z] hoặc null.</summary>
    private static float[]? ParseXyzTag(byte[] b, int off)
    {
        string type = Ascii(b, off, 4);
        if (type != "XYZ ") return null;
        float x = ReadS15Fixed16(b, off + 8);
        float y = ReadS15Fixed16(b, off + 12);
        float z = ReadS15Fixed16(b, off + 16);
        return new[] { x, y, z };
    }

    /// <summary>Số s15Fixed16 (big-endian, có dấu): giá trị thực = raw / 65536.</summary>
    private static float ReadS15Fixed16(byte[] b, int o)
    {
        int raw = (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        return raw / 65536f;
    }

    private static int ReadU32(byte[] b, int o)
        => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    private static string Ascii(byte[] b, int o, int len)
        => Encoding.ASCII.GetString(b, o, len);

    /// <summary>
    /// Đọc gamut từ ICC nhúng của 1 FILE (chỉ Identify metadata, không decode pixel -> nhanh). Trả null
    /// nếu không có ICC / không nhận diện được. Dùng cho UI gợi ý Input Profile mà không cần decode cả ảnh.
    /// Ưu tiên tên mô tả; nếu không khớp, thử ma trận colorant THẬT (chính xác, không phụ thuộc tên).
    /// </summary>
    public static ColorSpaces.Space? DetectSpaceFromFile(string path)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(path);
            var icc = info?.Metadata?.IccProfile;
            if (icc == null) return null;
            byte[] bytes = icc.ToByteArray();
            return GuessSpace(TryReadDescription(bytes))   // 1) theo tên mô tả
                ?? ColorSpaces.MatchSpace(TryReadRgbToXyzD65(bytes) ?? Array.Empty<float>()); // 2) theo ma trận
        }
        catch { return null; }
    }

    /// <summary>
    /// Đọc thông tin ICC nhúng để HIỂN THỊ cho người dùng: tên mô tả (nếu có) + gamut phát hiện
    /// (theo tên, fallback ma trận colorant). Trả (null, null) nếu không có ICC. Dùng cho UI minh bạch.
    /// </summary>
    public static (string? Description, ColorSpaces.Space? Space) ReadInfoFromFile(string path)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(path);
            var icc = info?.Metadata?.IccProfile;
            if (icc == null) return (null, null);
            byte[] bytes = icc.ToByteArray();
            string? desc = TryReadDescription(bytes);
            var space = GuessSpace(desc)
                ?? ColorSpaces.MatchSpace(TryReadRgbToXyzD65(bytes) ?? Array.Empty<float>());
            return (desc, space);
        }
        catch { return (null, null); }
    }
}
