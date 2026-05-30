using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ImageTool.Core;

namespace ImageTool.Shared;

public static class ExifReader
{
    public static CatalogImage ReadMetadata(string filePath)
    {
        var fi = new FileInfo(filePath);
        var img = new CatalogImage
        {
            FilePath = fi.FullName,
            FileName = fi.Name,
            FolderPath = fi.DirectoryName ?? "",
            FileSize = fi.Length
        };

        try
        {
            // Image.Identify CHỈ đọc header (kích thước + metadata) — KHÔNG decode pixel.
            // Nhanh hơn Image.Load hàng chục–trăm lần khi import (đặc biệt ảnh lớn).
            var info = Image.Identify(filePath);
            if (info == null) return img;
            img.Width = info.Width;
            img.Height = info.Height;

            var exif = info.Metadata?.ExifProfile;
            if (exif == null) return img;

            ApplyExif(exif, img);
        }
        catch
        {
            // Non-critical: if EXIF read fails, we still have file info
        }

        return img;
    }

    /// <summary>Áp các trường EXIF vào CatalogImage (tách ra để dùng chung Identify/Load).</summary>
    private static void ApplyExif(ExifProfile exif, CatalogImage img)
    {
        ReadGps(exif, img);

        foreach (var val in exif.Values)
        {
            var raw = val.GetValue()?.ToString()?.Trim('\0', ' ');
            if (string.IsNullOrEmpty(raw)) continue;

            var tag = val.Tag;

            if (tag == ExifTag.DateTimeOriginal)
                img.DateTaken ??= ParseDateTime(raw);
            else if (tag == ExifTag.DateTimeDigitized)
                img.DateTaken ??= ParseDateTime(raw);
            else if (tag == ExifTag.DateTime)
                img.DateTaken ??= ParseDateTime(raw);
            else if (tag == ExifTag.Make)
                img.CameraMake = raw;
            else if (tag == ExifTag.Model)
                img.CameraModel = raw;
            else if (tag == ExifTag.LensModel)
                img.LensModel = raw;
            else if (tag == ExifTag.FocalLength)
                img.FocalLength = ParseRational(raw);
            else if (tag == ExifTag.FNumber)
                img.Aperture = ParseRational(raw);
            else if (tag == ExifTag.ExposureTime)
                img.ShutterSpeed = FormatShutterSpeed(ParseRational(raw));
            else if (tag == ExifTag.ISOSpeedRatings)
            {
                if (int.TryParse(raw.Split(' ', '/')[0], out var iso))
                    img.Iso = iso;
            }
            else if (tag == ExifTag.Orientation)
            {
                if (int.TryParse(raw, out var orient))
                    img.Orientation = orient;
            }
        }
    }

    private static void ReadGps(ExifProfile exif, CatalogImage img)
    {
        if (TryReadGps(exif, out var lat, out var lon))
        {
            img.GpsLatitude = lat;
            img.GpsLongitude = lon;
        }
    }

    /// <summary>
    /// Đọc GPS lat/lon (decimal degrees) từ 1 ExifProfile đã có. Trả true nếu hợp lệ.
    /// Dùng chung cho catalog import và InfoPanel (khỏi load ảnh 2 lần).
    /// </summary>
    public static bool TryReadGps(ExifProfile exif, out double lat, out double lon)
    {
        lat = 0; lon = 0;
        try
        {
            if (!exif.TryGetValue(ExifTag.GPSLatitude, out var latVal) ||
                !exif.TryGetValue(ExifTag.GPSLongitude, out var lonVal))
                return false;

            var la = latVal?.Value as Rational[];
            var lo = lonVal?.Value as Rational[];
            if (la == null || la.Length < 3 || lo == null || lo.Length < 3) return false;

            string? latRef = null, lonRef = null;
            if (exif.TryGetValue(ExifTag.GPSLatitudeRef, out var lr)) latRef = lr?.Value?.ToString();
            if (exif.TryGetValue(ExifTag.GPSLongitudeRef, out var gr)) lonRef = gr?.Value?.ToString();

            double? latDd = GpsHelper.ToDecimalDegrees(la[0].ToDouble(), la[1].ToDouble(), la[2].ToDouble(), latRef);
            double? lonDd = GpsHelper.ToDecimalDegrees(lo[0].ToDouble(), lo[1].ToDouble(), lo[2].ToDouble(), lonRef);

            if (latDd.HasValue && lonDd.HasValue && GpsHelper.IsValid(latDd.Value, lonDd.Value))
            {
                lat = latDd.Value; lon = lonDd.Value;
                return true;
            }
        }
        catch (Exception ex) { AppLog.Warn("ExifReader.Gps", ex.Message); }
        return false;
    }

    private static DateTime? ParseDateTime(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    private static double? ParseRational(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Contains('/'))
        {
            var parts = s.Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], out var num) && double.TryParse(parts[1], out var den) && den > 0)
                return num / den;
        }
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }

    private static string? FormatShutterSpeed(double? value)
    {
        if (value == null || value <= 0) return null;
        if (value >= 1) return $"{value:F1}s";
        return $"1/{(int)Math.Round(1.0 / value.Value)}";
    }
}
