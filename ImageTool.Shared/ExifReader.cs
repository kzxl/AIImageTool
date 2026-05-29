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
            using var image = Image.Load(filePath);
            img.Width = image.Width;
            img.Height = image.Height;

            var exif = image.Metadata?.ExifProfile;
            if (exif == null) return img;

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
        catch
        {
            // Non-critical: if EXIF read fails, we still have file info
        }

        return img;
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
