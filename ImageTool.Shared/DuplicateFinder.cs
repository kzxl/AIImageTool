using System;
using System.Collections.Generic;
using ImageTool.Imaging;

namespace ImageTool.Shared;

/// <summary>
/// Tìm ảnh trùng / gần trùng (#1) trong 1 danh sách đường dẫn bằng perceptual hash (dHash).
/// Decode nhẹ qua ImageDecoderRegistry rồi hash; gom nhóm theo khoảng cách Hamming. Ảnh lỗi decode
/// bị bỏ qua. Trả các nhóm (mỗi nhóm &gt;1 ảnh) dưới dạng đường dẫn để UI chọn/đánh dấu.
/// </summary>
public static class DuplicateFinder
{
    /// <summary>
    /// Hash + gom nhóm. <paramref name="threshold"/> = khoảng cách Hamming tối đa (mặc định 10 ~ giống 84%).
    /// Trả danh sách nhóm; mỗi nhóm là danh sách path gần trùng nhau.
    /// </summary>
    public static List<List<string>> FindGroups(
        IReadOnlyList<string> paths, ImageDecoderRegistry decoders, int threshold = 10)
    {
        var validPaths = new List<string>();
        var hashes = new List<ulong>();
        foreach (var p in paths)
        {
            try
            {
                if (!decoders.CanDecode(p)) continue;
                var decoded = decoders.Decode(p);
                hashes.Add(PerceptualHash.DHash(decoded.Image));
                validPaths.Add(p);
            }
            catch (Exception ex) { AppLog.Warn("DuplicateFinder", $"bỏ qua {p}: {ex.Message}"); }
        }

        var idxGroups = PerceptualHash.GroupSimilar(hashes, threshold);
        var result = new List<List<string>>();
        foreach (var g in idxGroups)
        {
            var group = new List<string>(g.Count);
            foreach (var i in g) group.Add(validPaths[i]);
            result.Add(group);
        }
        return result;
    }
}
