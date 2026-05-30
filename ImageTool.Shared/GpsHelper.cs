using System;
using System.Globalization;

namespace ImageTool.Shared;

/// <summary>
/// Chuyển toạ độ GPS trong EXIF (8.5). EXIF lưu lat/lon dạng độ-phút-giây (3 rational) + ref
/// (N/S/E/W). Helper này quy về decimal degrees và sinh link bản đồ. Thuần toán -> test được.
/// </summary>
public static class GpsHelper
{
    /// <summary>
    /// Quy độ-phút-giây + hướng tham chiếu về decimal degrees.
    /// ref "S"/"W" -> giá trị âm. Trả null nếu dữ liệu không hợp lệ.
    /// </summary>
    public static double? ToDecimalDegrees(double degrees, double minutes, double seconds, string? reference)
    {
        if (double.IsNaN(degrees) || double.IsNaN(minutes) || double.IsNaN(seconds)) return null;
        double dd = Math.Abs(degrees) + minutes / 60.0 + seconds / 3600.0;
        if (dd > 180.0) return null; // ngoài phạm vi hợp lệ
        var r = reference?.Trim().ToUpperInvariant();
        if (r == "S" || r == "W") dd = -dd;
        return dd;
    }

    /// <summary>True nếu lat/lon nằm trong phạm vi hợp lệ.</summary>
    public static bool IsValid(double lat, double lon)
        => lat is >= -90 and <= 90 && lon is >= -180 and <= 180 && !(lat == 0 && lon == 0);

    /// <summary>Link Google Maps cho 1 toạ độ (decimal degrees).</summary>
    public static string GoogleMapsUrl(double lat, double lon)
        => $"https://www.google.com/maps/search/?api=1&query={lat.ToString("0.000000", CultureInfo.InvariantCulture)},{lon.ToString("0.000000", CultureInfo.InvariantCulture)}";

    /// <summary>Link OpenStreetMap cho 1 toạ độ.</summary>
    public static string OpenStreetMapUrl(double lat, double lon)
        => $"https://www.openstreetmap.org/?mlat={lat.ToString("0.000000", CultureInfo.InvariantCulture)}&mlon={lon.ToString("0.000000", CultureInfo.InvariantCulture)}#map=15/{lat.ToString("0.0000", CultureInfo.InvariantCulture)}/{lon.ToString("0.0000", CultureInfo.InvariantCulture)}";

    /// <summary>Hiển thị thân thiện: "21.028511, 105.804817" (6 chữ số thập phân).</summary>
    public static string Format(double lat, double lon)
        => $"{lat.ToString("0.000000", CultureInfo.InvariantCulture)}, {lon.ToString("0.000000", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Parse chuỗi rational EXIF GPS dạng "d/1 m/1 s/100" hoặc "d, m, s" về 3 thành phần.
    /// Trả null nếu không đủ 3 thành phần.
    /// </summary>
    public static (double D, double M, double S)? ParseDms(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Replace(",", " ").Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        double? d = Rational(parts[0]);
        double? m = Rational(parts[1]);
        double? s = Rational(parts[2]);
        if (d == null || m == null || s == null) return null;
        return (d.Value, m.Value, s.Value);
    }

    private static double? Rational(string token)
    {
        if (token.Contains('/'))
        {
            var p = token.Split('/');
            if (p.Length == 2 &&
                double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
                double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) && den != 0)
                return n / den;
            return null;
        }
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
