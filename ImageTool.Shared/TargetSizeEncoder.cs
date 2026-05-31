using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;

namespace ImageTool.Shared;

/// <summary>
/// Nén ảnh tới DUNG LƯỢNG MỤC TIÊU (target KB) bằng tìm kiếm nhị phân trên quality (jpg/webp lossy).
/// Encode thử vào bộ nhớ ở mỗi bước (không ghi đĩa), chọn quality cao nhất mà ≤ target. Dừng sớm khi
/// hội tụ. PNG/TIFF (lossless) không áp dụng quality -> trả về encode mặc định 1 lần.
/// </summary>
public static class TargetSizeEncoder
{
    /// <summary>Số lần encode thử tối đa (mỗi bước thu hẹp một nửa khoảng quality).</summary>
    public const int MaxIterations = 8;

    public sealed class Result
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public int Quality { get; init; }
        public long Bytes => Data.LongLength;
        /// <summary>True nếu đạt ≤ target (hoặc target=0 = bỏ qua, luôn true).</summary>
        public bool MetTarget { get; init; }
    }

    /// <summary>
    /// Tìm quality cao nhất cho <paramref name="format"/> sao cho file ≤ <paramref name="targetBytes"/>.
    /// targetBytes ≤ 0 -> không giới hạn (encode 1 lần với quality trong params). Chỉ jpg/webp-lossy
    /// đáp ứng được quality search; định dạng khác encode 1 lần.
    /// </summary>
    public static Result Encode(Image image, string format, IReadOnlyDictionary<string, string> baseParams, long targetBytes)
    {
        string f = (format ?? "png").ToLowerInvariant();
        bool qualityControllable = f is "jpg" or "jpeg" or "webp";

        if (targetBytes <= 0 || !qualityControllable || !IsLossyWebp(f, baseParams))
        {
            var data = EncodeAt(image, f, baseParams, null);
            return new Result { Data = data, Quality = ReadQuality(baseParams), MetTarget = true };
        }

        int lo = 5, hi = 100;
        byte[]? best = null;
        int bestQ = lo;

        // Encode ở hi trước: nếu đã ≤ target thì lấy luôn (chất lượng tối đa).
        var hiData = EncodeAt(image, f, baseParams, hi);
        if (hiData.LongLength <= targetBytes)
            return new Result { Data = hiData, Quality = hi, MetTarget = true };

        for (int i = 0; i < MaxIterations && lo <= hi; i++)
        {
            int mid = (lo + hi) / 2;
            var data = EncodeAt(image, f, baseParams, mid);
            if (data.LongLength <= targetBytes)
            {
                best = data; bestQ = mid;   // đạt -> thử cao hơn.
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;               // vượt -> thử thấp hơn.
            }
        }

        if (best != null)
            return new Result { Data = best, Quality = bestQ, MetTarget = true };

        // Không bao giờ đạt target (target quá nhỏ) -> trả bản quality thấp nhất.
        var lowest = EncodeAt(image, f, baseParams, 5);
        return new Result { Data = lowest, Quality = 5, MetTarget = false };
    }

    private static bool IsLossyWebp(string f, IReadOnlyDictionary<string, string> p)
    {
        if (f != "webp") return true; // jpg luôn lossy.
        string mode = p.TryGetValue("webpMode", out var m) ? m.ToLowerInvariant() : "lossy";
        return mode == "lossy"; // lossless/nearlossless không điều khiển bằng quality search.
    }

    private static int ReadQuality(IReadOnlyDictionary<string, string> p)
        => p.TryGetValue("quality", out var s) && int.TryParse(s, out var q) ? q : 90;

    private static byte[] EncodeAt(Image image, string format, IReadOnlyDictionary<string, string> baseParams, int? quality)
    {
        var p = new Dictionary<string, string>(baseParams, StringComparer.OrdinalIgnoreCase);
        if (quality.HasValue) p["quality"] = quality.Value.ToString();
        var encoder = EncoderFactory.Create(format, p);
        using var ms = new MemoryStream();
        image.Save(ms, encoder);
        return ms.ToArray();
    }
}
