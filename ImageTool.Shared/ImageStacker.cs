using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageTool.Shared;

/// <summary>
/// Gom nhóm ảnh thành "stack" (8.7) — bracket/burst phát hiện theo khoảng cách thời gian chụp.
/// Ảnh chụp liên tiếp trong vòng <c>thresholdSeconds</c> được gom 1 stack (giống auto-stack by
/// capture time của Lightroom). Thuần logic trên (path, timestamp) -> unit test trực tiếp.
/// </summary>
public static class ImageStacker
{
    public sealed class Stack
    {
        /// <summary>Các path trong stack, theo thứ tự thời gian tăng dần.</summary>
        public List<string> Items { get; } = new();
        /// <summary>Ảnh đại diện (cover) — mặc định ảnh đầu stack.</summary>
        public string Cover => Items.Count > 0 ? Items[0] : "";
        public bool IsStack => Items.Count > 1;
    }

    /// <summary>
    /// Gom theo thời gian. <paramref name="items"/> = (path, captureTime). Sắp theo thời gian rồi
    /// cắt stack mới khi khoảng cách tới ảnh trước &gt; thresholdSeconds. Ảnh đơn lẻ thành stack 1 phần tử.
    /// </summary>
    public static List<Stack> StackByTime(IEnumerable<(string Path, DateTime Time)> items, double thresholdSeconds = 2.0)
    {
        var sorted = items.Where(i => !string.IsNullOrEmpty(i.Path))
                          .OrderBy(i => i.Time).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                          .ToList();
        var stacks = new List<Stack>();
        if (sorted.Count == 0) return stacks;

        var threshold = TimeSpan.FromSeconds(Math.Max(0, thresholdSeconds));
        Stack current = new();
        current.Items.Add(sorted[0].Path);
        DateTime prev = sorted[0].Time;

        for (int i = 1; i < sorted.Count; i++)
        {
            var (path, time) = sorted[i];
            if (time - prev <= threshold)
            {
                current.Items.Add(path);
            }
            else
            {
                stacks.Add(current);
                current = new Stack();
                current.Items.Add(path);
            }
            prev = time;
        }
        stacks.Add(current);
        return stacks;
    }

    /// <summary>
    /// Gom theo tên file cùng "gốc" (vd IMG_1234, IMG_1234-Edit, IMG_1234_HDR) — hữu ích khi
    /// không có metadata thời gian. So khớp phần đầu tên trước dấu phân tách/hậu tố.
    /// </summary>
    public static List<Stack> StackByBaseName(IEnumerable<string> paths)
    {
        var groups = new Dictionary<string, Stack>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var p in paths.Where(p => !string.IsNullOrEmpty(p)))
        {
            string baseKey = BaseName(System.IO.Path.GetFileNameWithoutExtension(p));
            if (!groups.TryGetValue(baseKey, out var st))
            {
                st = new Stack();
                groups[baseKey] = st;
                order.Add(baseKey);
            }
            st.Items.Add(p);
        }
        return order.Select(k => groups[k]).ToList();
    }

    /// <summary>
    /// Trích "gốc" tên cho stacking: bỏ MỘT hậu tố chỉnh sửa ở cuối (sau '-' hoặc '_') nếu hậu tố
    /// đó KHÔNG phải toàn số. Giữ ID số (IMG_1234 nguyên), gộp biến thể chỉnh sửa:
    ///   "IMG_1234" -> "IMG_1234"; "IMG_1234-Edit" -> "IMG_1234"; "photo-edit" -> "photo"; "plain" -> "plain".
    /// </summary>
    public static string BaseName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        int cut = name.LastIndexOfAny(new[] { '-', '_' });
        if (cut <= 0 || cut >= name.Length - 1) return name;
        string suffix = name.Substring(cut + 1);
        bool numeric = suffix.Length > 0 && suffix.All(char.IsDigit);
        return numeric ? name : name.Substring(0, cut);
    }
}
