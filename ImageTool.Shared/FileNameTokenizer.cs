using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ImageTool.Shared;

/// <summary>
/// Engine sinh tên file theo pattern token (13.6). Dùng chung cho Export (đổi tên khi xuất) và
/// Batch Rename (đổi tên hàng loạt tại chỗ). Token hỗ trợ:
///   {name}      tên gốc (không đuôi)
///   {ext}       đuôi (không chấm)
///   {n}         số thứ tự (1-based), đệm 0 theo {n:000}
///   {n:0000}    số thứ tự đệm theo số chữ số trong format
///   {date}      ngày hiện tại yyyy-MM-dd
///   {date:FMT}  ngày theo format tuỳ chọn (vd {date:yyyyMMdd})
///   {time}      giờ HHmmss
///   {w} {h}     kích thước (nếu cung cấp)
///   {parent}    tên thư mục cha
///   {upper:..} {lower:..} không hỗ trợ (giữ đơn giản)
///
/// Thuần xử lý chuỗi -> unit test trực tiếp. Tự loại ký tự không hợp lệ cho tên file.
/// </summary>
public static class FileNameTokenizer
{
    public sealed class Context
    {
        public string OriginalName { get; set; } = "";  // không đuôi
        public string Extension { get; set; } = "";       // không chấm
        public int Index { get; set; } = 1;               // 1-based
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string ParentFolder { get; set; } = "";
        public DateTime Now { get; set; } = DateTime.Now;
    }

    /// <summary>Sinh tên file (chưa gồm đuôi nếu pattern không có {ext}). Token không rõ giữ nguyên text.</summary>
    public static string Resolve(string pattern, Context ctx)
    {
        if (string.IsNullOrEmpty(pattern)) pattern = "{name}";
        var sb = new StringBuilder();
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '{')
            {
                int end = pattern.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(pattern.Substring(i)); break; }
                string token = pattern.Substring(i + 1, end - i - 1);
                sb.Append(ResolveToken(token, ctx));
                i = end + 1;
            }
            else { sb.Append(c); i++; }
        }
        return Sanitize(sb.ToString());
    }

    private static string ResolveToken(string token, Context ctx)
    {
        // tách "key:arg".
        string key = token, arg = "";
        int colon = token.IndexOf(':');
        if (colon >= 0) { key = token.Substring(0, colon); arg = token.Substring(colon + 1); }

        switch (key.ToLowerInvariant())
        {
            case "name": return ctx.OriginalName;
            case "ext": return ctx.Extension;
            case "n":
            case "num":
            case "seq":
                if (!string.IsNullOrEmpty(arg))
                    return ctx.Index.ToString(arg, CultureInfo.InvariantCulture);
                return ctx.Index.ToString(CultureInfo.InvariantCulture);
            case "date":
                return ctx.Now.ToString(string.IsNullOrEmpty(arg) ? "yyyy-MM-dd" : arg, CultureInfo.InvariantCulture);
            case "time":
                return ctx.Now.ToString(string.IsNullOrEmpty(arg) ? "HHmmss" : arg, CultureInfo.InvariantCulture);
            case "w": return ctx.Width?.ToString(CultureInfo.InvariantCulture) ?? "";
            case "h": return ctx.Height?.ToString(CultureInfo.InvariantCulture) ?? "";
            case "parent": return ctx.ParentFolder;
            default: return "{" + token + "}"; // token lạ: giữ nguyên để user thấy lỗi
        }
    }

    /// <summary>Loại ký tự không hợp lệ cho tên file (giữ chấm và gạch).</summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>
    /// Trả về 1 đường dẫn không đụng file có sẵn trên đĩa: nếu <paramref name="path"/> chưa tồn tại thì
    /// trả nguyên; nếu đã có thì thêm hậu tố " (1)", " (2)"... trước phần đuôi cho tới khi tìm được tên trống.
    /// Tránh ghi đè im lặng khi export. Thuần đường dẫn nên test được (dùng <paramref name="exists"/> bơm vào).
    /// </summary>
    public static string EnsureUniquePath(string path, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!exists(path)) return path;
        string dir = Path.GetDirectoryName(path) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path); // gồm dấu chấm
        for (int i = 1; i < 100000; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!exists(candidate)) return candidate;
        }
        return path; // fallback cực hiếm
    }

    /// <summary>
    /// Sinh danh sách tên mới cho 1 loạt file (batch rename). Tự đảm bảo tên không trùng nhau
    /// trong cùng lô (thêm hậu tố _1, _2... nếu đụng). Trả map oldPath -> newFileName (gồm đuôi).
    /// </summary>
    public static List<(string OldPath, string NewName)> ResolveBatch(
        IReadOnlyList<string> paths, string pattern, int startIndex = 1, DateTime? now = null)
    {
        var result = new List<(string, string)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var when = now ?? DateTime.Now;
        for (int k = 0; k < paths.Count; k++)
        {
            var path = paths[k];
            string ext = Path.GetExtension(path).TrimStart('.');
            var ctx = new Context
            {
                OriginalName = Path.GetFileNameWithoutExtension(path),
                Extension = ext,
                Index = startIndex + k,
                ParentFolder = new DirectoryInfo(Path.GetDirectoryName(path) ?? ".").Name,
                Now = when,
            };
            string baseName = Resolve(pattern, ctx);
            // đảm bảo có đuôi.
            string finalName = baseName;
            if (!string.IsNullOrEmpty(ext) && !finalName.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
                finalName = baseName + "." + ext;

            // chống trùng trong lô.
            string candidate = finalName;
            int dup = 1;
            while (used.Contains(candidate))
            {
                string stem = Path.GetFileNameWithoutExtension(finalName);
                string e = Path.GetExtension(finalName);
                candidate = $"{stem}_{dup}{e}";
                dup++;
            }
            used.Add(candidate);
            result.Add((path, candidate));
        }
        return result;
    }
}
