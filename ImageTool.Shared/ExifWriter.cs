using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace ImageTool.Shared;

/// <summary>
/// Ghi/sửa các trường EXIF cơ bản vào file ảnh (gộp từ plugin MetaEditor cũ vào InfoPanel).
/// Hỗ trợ: ImageDescription, Artist, Copyright, Software, Make, Model. Ghi đè trực tiếp file gốc.
/// Trả về true nếu lưu thành công. Logic tách riêng để test (đọc lại xác nhận).
/// </summary>
public static class ExifWriter
{
    /// <summary>Các khoá hỗ trợ sửa (tên hiển thị = tên EXIF tag).</summary>
    public static readonly string[] EditableFields =
    {
        "ImageDescription", "Artist", "Copyright", "Software", "Make", "Model"
    };

    /// <summary>
    /// Áp các giá trị (key = tên field trong <see cref="EditableFields"/>) vào EXIF của file rồi lưu.
    /// Bỏ qua key lạ. Trả true nếu lưu được.
    /// </summary>
    public static bool Write(string imagePath, IReadOnlyDictionary<string, string> values)
    {
        try
        {
            using var image = Image.Load(imagePath);
            var profile = image.Metadata.ExifProfile ?? new ExifProfile();

            foreach (var kv in values)
                ApplyField(profile, kv.Key, kv.Value ?? "");

            image.Metadata.ExifProfile = profile;
            image.Save(imagePath);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("ExifWriter.Write", imagePath, ex);
            return false;
        }
    }

    /// <summary>Áp 1 field vào profile. True nếu field được hỗ trợ.</summary>
    public static bool ApplyField(ExifProfile profile, string field, string value)
    {
        switch (field)
        {
            case "ImageDescription": profile.SetValue(ExifTag.ImageDescription, value); return true;
            case "Artist": profile.SetValue(ExifTag.Artist, value); return true;
            case "Copyright": profile.SetValue(ExifTag.Copyright, value); return true;
            case "Software": profile.SetValue(ExifTag.Software, value); return true;
            case "Make": profile.SetValue(ExifTag.Make, value); return true;
            case "Model": profile.SetValue(ExifTag.Model, value); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Trả về bản sao profile đã dọn tag không còn đúng sau khi bake/resize. Tách riêng (nhận profile)
    /// để unit test được mà không cần file. Null nếu nguồn null.
    /// </summary>
    public static ExifProfile? SanitizeProfile(ExifProfile? source)
    {
        if (source == null) return null;
        var p = source.DeepClone();
        // Orientation: ảnh đã được xoay đúng ở mức pixel -> đặt về Normal (1) để viewer không xoay lại.
        p.SetValue(ExifTag.Orientation, (ushort)1);
        // Kích thước cũ không còn đúng sau resize/crop -> bỏ để viewer dùng pixel thật.
        p.RemoveValue(ExifTag.PixelXDimension);
        p.RemoveValue(ExifTag.PixelYDimension);
        return p;
    }

    /// <summary>
    /// Sao chép EXIF từ file nguồn sang ảnh đích (đã render/bake), dọn tag không còn đúng. Giữ lại
    /// camera/lens/exposure/ISO/ngày chụp/GPS. No-op nếu nguồn không có EXIF.
    /// </summary>
    public static void PreserveExif(string sourcePath, Image target)
    {
        try
        {
            using var src = Image.Load(sourcePath);
            var p = SanitizeProfile(src.Metadata.ExifProfile);
            if (p != null) target.Metadata.ExifProfile = p;
        }
        catch (Exception ex) { AppLog.Warn("ExifWriter.PreserveExif", $"{sourcePath}: {ex.Message}"); }
    }

    /// <summary>Đọc giá trị hiện tại của các field hỗ trợ (rỗng nếu chưa có). Dùng để fill form.</summary>
    public static Dictionary<string, string> ReadEditable(string imagePath)
    {
        var result = new Dictionary<string, string>();
        foreach (var f in EditableFields) result[f] = "";
        try
        {
            using var image = Image.Load(imagePath);
            var profile = image.Metadata.ExifProfile;
            if (profile == null) return result;
            if (profile.TryGetValue(ExifTag.ImageDescription, out var d) && d?.Value != null) result["ImageDescription"] = d.Value.ToString() ?? "";
            if (profile.TryGetValue(ExifTag.Artist, out var a) && a?.Value != null) result["Artist"] = a.Value.ToString() ?? "";
            if (profile.TryGetValue(ExifTag.Copyright, out var c) && c?.Value != null) result["Copyright"] = c.Value.ToString() ?? "";
            if (profile.TryGetValue(ExifTag.Software, out var s) && s?.Value != null) result["Software"] = s.Value.ToString() ?? "";
            if (profile.TryGetValue(ExifTag.Make, out var mk) && mk?.Value != null) result["Make"] = mk.Value.ToString() ?? "";
            if (profile.TryGetValue(ExifTag.Model, out var md) && md?.Value != null) result["Model"] = md.Value.ToString() ?? "";
        }
        catch (Exception ex) { AppLog.Warn("ExifWriter.ReadEditable", $"{imagePath}: {ex.Message}"); }
        return result;
    }
}
