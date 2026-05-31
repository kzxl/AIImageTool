using System;
using System.Collections.Generic;
using System.Globalization;
using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Tự sinh keyword từ EXIF (#3) — phân cấp theo cú pháp "A/B": camera, lens, dải ISO, dải tiêu cự,
/// năm/tháng chụp. Dùng để gán nhanh keyword khi import hoặc qua menu. Thuần logic -> unit test trực tiếp.
/// </summary>
public static class ExifAutoTagger
{
    /// <summary>Sinh danh sách keyword phân cấp (chuẩn hoá) từ metadata. Không trùng, bỏ giá trị rỗng.</summary>
    public static List<string> Generate(CatalogImage img)
    {
        var tags = new List<string>();
        void Add(string t) { if (!string.IsNullOrWhiteSpace(t) && !tags.Contains(t)) tags.Add(t); }

        // Camera: "Camera/<Make>/<Model>"
        if (!string.IsNullOrWhiteSpace(img.CameraMake))
        {
            string make = Clean(img.CameraMake!);
            Add($"Camera/{make}");
            if (!string.IsNullOrWhiteSpace(img.CameraModel))
                Add($"Camera/{make}/{Clean(img.CameraModel!)}");
        }
        else if (!string.IsNullOrWhiteSpace(img.CameraModel))
        {
            Add($"Camera/{Clean(img.CameraModel!)}");
        }

        // Lens: "Lens/<Model>"
        if (!string.IsNullOrWhiteSpace(img.LensModel))
            Add($"Lens/{Clean(img.LensModel!)}");

        // ISO range: "ISO/<bucket>"
        if (img.Iso is int iso && iso > 0)
            Add($"ISO/{IsoBucket(iso)}");

        // Focal length range: "Focal/<bucket>"
        if (img.FocalLength is double fl && fl > 0)
            Add($"Focal/{FocalBucket(fl)}");

        // Ngày chụp: "Date/<yyyy>" + "Date/<yyyy>/<MM>"
        if (img.DateTaken is DateTime dt)
        {
            Add($"Date/{dt.Year}");
            Add($"Date/{dt.Year}/{dt.Month:00}");
        }

        return tags;
    }

    /// <summary>Phân nhóm ISO theo dải nhiếp ảnh quen thuộc.</summary>
    public static string IsoBucket(int iso)
    {
        if (iso <= 100) return "100";
        if (iso <= 200) return "100-200";
        if (iso <= 400) return "200-400";
        if (iso <= 800) return "400-800";
        if (iso <= 1600) return "800-1600";
        if (iso <= 3200) return "1600-3200";
        if (iso <= 6400) return "3200-6400";
        return "6400+";
    }

    /// <summary>Phân nhóm tiêu cự theo loại ống kính.</summary>
    public static string FocalBucket(double mm)
    {
        if (mm < 16) return "Ultra-wide (<16mm)";
        if (mm < 35) return "Wide (16-35mm)";
        if (mm < 70) return "Normal (35-70mm)";
        if (mm < 135) return "Short tele (70-135mm)";
        if (mm < 300) return "Tele (135-300mm)";
        return "Super-tele (300mm+)";
    }

    private static string Clean(string s)
        => s.Trim().Replace('/', '-').Replace('\\', '-');
}
