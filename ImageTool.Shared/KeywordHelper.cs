using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageTool.Shared;

/// <summary>
/// Tiện ích keyword/tag phân cấp (8.2). Keyword dùng dấu "/" làm phân cấp, ví dụ
/// "Animal/Dog/Puppy". Hỗ trợ:
///  - Chuẩn hoá (trim, bỏ rỗng, gộp trùng).
///  - Mở rộng tổ tiên: "Animal/Dog" -> ["Animal", "Animal/Dog"] để lọc theo nhánh cha.
///  - Dựng cây phân cấp từ danh sách phẳng (cho UI hiển thị).
///  - So khớp: 1 ảnh có keyword "Animal/Dog/Puppy" khớp tìm "Animal" (cha) và "Dog" (đoạn).
///
/// Thuần logic, không phụ thuộc IO/UI -> unit test trực tiếp.
/// </summary>
public static class KeywordHelper
{
    public const char Separator = '/';

    /// <summary>Chuẩn hoá 1 keyword: trim từng đoạn, bỏ đoạn rỗng, nối lại. Trả null nếu rỗng.</summary>
    public static string? Normalize(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return null;
        var parts = keyword.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                           .Select(p => p.Trim())
                           .Where(p => p.Length > 0)
                           .ToArray();
        return parts.Length == 0 ? null : string.Join(Separator, parts);
    }

    /// <summary>Chuẩn hoá + bỏ trùng (giữ thứ tự xuất hiện đầu) cho 1 danh sách keyword.</summary>
    public static List<string> NormalizeList(IEnumerable<string>? keywords)
    {
        var result = new List<string>();
        if (keywords == null) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in keywords)
        {
            var n = Normalize(k);
            if (n != null && seen.Add(n)) result.Add(n);
        }
        return result;
    }

    /// <summary>
    /// Mở rộng tổ tiên của 1 keyword phân cấp. "A/B/C" -> ["A", "A/B", "A/B/C"].
    /// Dùng để: gắn tag thì gắn cả nhánh cha (search "A" ra mọi ảnh dưới A).
    /// </summary>
    public static List<string> ExpandAncestors(string keyword)
    {
        var result = new List<string>();
        var n = Normalize(keyword);
        if (n == null) return result;
        var parts = n.Split(Separator);
        for (int i = 0; i < parts.Length; i++)
            result.Add(string.Join(Separator, parts.Take(i + 1)));
        return result;
    }

    /// <summary>Đoạn lá (segment cuối) của keyword phân cấp. "A/B/C" -> "C".</summary>
    public static string LeafName(string keyword)
    {
        var n = Normalize(keyword);
        if (n == null) return "";
        int idx = n.LastIndexOf(Separator);
        return idx < 0 ? n : n.Substring(idx + 1);
    }

    /// <summary>
    /// 1 ảnh có tập <paramref name="imageKeywords"/> có khớp truy vấn <paramref name="query"/> không?
    /// Khớp nếu: query là tiền tố nhánh (ảnh ở dưới nhánh đó), hoặc khớp 1 segment bất kỳ
    /// (không phân biệt hoa thường). Ví dụ ảnh "Animal/Dog" khớp "animal", "dog", "Animal/Dog".
    /// </summary>
    public static bool Matches(IEnumerable<string> imageKeywords, string query)
    {
        var q = Normalize(query);
        if (q == null) return false;
        foreach (var kw in imageKeywords)
        {
            var n = Normalize(kw);
            if (n == null) continue;
            // khớp nhánh: query là tiền tố phân cấp của keyword (hoặc bằng).
            if (n.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith(q + Separator, StringComparison.OrdinalIgnoreCase))
                return true;
            // khớp segment đơn: query không chứa "/" và trùng 1 đoạn.
            if (!q.Contains(Separator))
            {
                var segs = n.Split(Separator);
                if (segs.Any(s => s.Equals(q, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        return false;
    }

    /// <summary>1 nút cây keyword (cho UI hiển thị phân cấp).</summary>
    public sealed class Node
    {
        public string Name { get; set; } = "";      // segment (lá)
        public string FullPath { get; set; } = "";   // đường dẫn đầy đủ
        public int Count { get; set; }               // số ảnh gắn (gồm nhánh con)
        public List<Node> Children { get; } = new();
    }

    /// <summary>
    /// Dựng cây phân cấp từ danh sách keyword phẳng kèm số đếm. Mỗi keyword đóng góp count cho
    /// chính nó và mọi tổ tiên. Trả danh sách node gốc (cấp 1), con sắp theo tên.
    /// </summary>
    public static List<Node> BuildTree(IEnumerable<KeyValuePair<string, int>> keywordCounts)
    {
        var roots = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in keywordCounts)
        {
            var n = Normalize(kv.Key);
            if (n == null) continue;
            var parts = n.Split(Separator);
            string path = "";
            Dictionary<string, Node> level = roots;
            Node? parent = null;
            for (int i = 0; i < parts.Length; i++)
            {
                path = i == 0 ? parts[0] : path + Separator + parts[i];
                if (!byPath.TryGetValue(path, out var node))
                {
                    node = new Node { Name = parts[i], FullPath = path };
                    byPath[path] = node;
                    level[parts[i]] = node;
                    parent?.Children.Add(node);
                }
                node.Count += kv.Value; // count cộng cho nhánh cha
                parent = node;
                level = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase); // không dùng lại; con quản qua byPath
            }
        }

        var result = roots.Values.ToList();
        SortRecursive(result);
        return result;
    }

    private static void SortRecursive(List<Node> nodes)
    {
        nodes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var n in nodes) SortRecursive(n.Children);
    }
}
