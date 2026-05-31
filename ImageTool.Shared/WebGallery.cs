using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ImageTool.Shared;

/// <summary>
/// Xuất web gallery HTML (#11) — sinh trang HTML responsive (grid + lightbox click phóng to) từ 1
/// danh sách ảnh: tạo thumbnail JPEG + bản "large" trong thư mục con, viết index.html tự chứa
/// (CSS/JS inline, không phụ thuộc mạng). Phần dựng HTML tách riêng để unit test (không cần ảnh thật).
/// </summary>
public static class WebGallery
{
    public sealed class Options
    {
        public string Title { get; set; } = "Gallery";
        public int ThumbSize { get; set; } = 400;
        public int LargeSize { get; set; } = 1600;
        public int Quality { get; set; } = 85;
        public int Columns { get; set; } = 4;
    }

    public sealed class Entry
    {
        public string Thumb { get; init; } = "";   // đường dẫn tương đối
        public string Large { get; init; } = "";
        public string Caption { get; init; } = "";
    }

    /// <summary>
    /// Render gallery: copy/resize ảnh + ghi index.html vào <paramref name="outDir"/>. Trả đường dẫn
    /// index.html. Ảnh lỗi bị bỏ qua. Trả về số ảnh đã xuất qua <paramref name="count"/>.
    /// </summary>
    public static string Render(IReadOnlyList<string> paths, string outDir, Options opt, out int count)
    {
        Directory.CreateDirectory(outDir);
        string thumbDir = Path.Combine(outDir, "thumbs");
        string largeDir = Path.Combine(outDir, "large");
        Directory.CreateDirectory(thumbDir);
        Directory.CreateDirectory(largeDir);

        var entries = new List<Entry>();
        int idx = 0;
        foreach (var p in paths)
        {
            try
            {
                using var img = Image.Load(p);
                string baseName = $"{idx:000}_{SanitizeFileName(Path.GetFileNameWithoutExtension(p))}";
                string thumbRel = $"thumbs/{baseName}.jpg";
                string largeRel = $"large/{baseName}.jpg";

                SaveResized(img, Path.Combine(outDir, thumbRel), opt.ThumbSize, opt.Quality);
                SaveResized(img, Path.Combine(outDir, largeRel), opt.LargeSize, opt.Quality);

                entries.Add(new Entry { Thumb = thumbRel, Large = largeRel, Caption = Path.GetFileName(p) });
                idx++;
            }
            catch (Exception ex) { AppLog.Warn("WebGallery", $"bỏ qua {p}: {ex.Message}"); }
        }

        count = entries.Count;
        string html = BuildHtml(entries, opt);
        string indexPath = Path.Combine(outDir, "index.html");
        File.WriteAllText(indexPath, html, new UTF8Encoding(false));
        return indexPath;
    }

    /// <summary>Dựng HTML tự chứa (CSS/JS inline) — thuần chuỗi, test trực tiếp.</summary>
    public static string BuildHtml(IReadOnlyList<Entry> entries, Options opt)
    {
        int cols = Math.Clamp(opt.Columns, 1, 8);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append($"<title>{Esc(opt.Title)}</title>\n");
        sb.Append("<style>\n");
        sb.Append("*{box-sizing:border-box}body{margin:0;background:#111;color:#ddd;font-family:system-ui,Segoe UI,sans-serif}\n");
        sb.Append("h1{font-weight:600;font-size:20px;padding:18px 20px;margin:0}\n");
        sb.Append($".grid{{display:grid;grid-template-columns:repeat({cols},1fr);gap:8px;padding:12px}}\n");
        sb.Append(".grid img{width:100%;height:100%;object-fit:cover;border-radius:4px;cursor:pointer;display:block;aspect-ratio:1/1}\n");
        sb.Append(".lb{position:fixed;inset:0;background:rgba(0,0,0,.92);display:none;align-items:center;justify-content:center;flex-direction:column;z-index:10}\n");
        sb.Append(".lb.open{display:flex}.lb img{max-width:94vw;max-height:86vh;object-fit:contain}\n");
        sb.Append(".lb .cap{margin-top:10px;font-size:13px;color:#aaa}.lb .x{position:absolute;top:14px;right:20px;font-size:28px;color:#ccc;cursor:pointer}\n");
        sb.Append("</style>\n</head>\n<body>\n");
        sb.Append($"<h1>{Esc(opt.Title)}</h1>\n<div class=\"grid\">\n");
        foreach (var e in entries)
            sb.Append($"<img src=\"{Esc(e.Thumb)}\" data-large=\"{Esc(e.Large)}\" data-cap=\"{Esc(e.Caption)}\" alt=\"{Esc(e.Caption)}\">\n");
        sb.Append("</div>\n");
        sb.Append("<div class=\"lb\" id=\"lb\"><span class=\"x\" id=\"x\">&times;</span><img id=\"lbimg\" src=\"\"><div class=\"cap\" id=\"cap\"></div></div>\n");
        sb.Append("<script>\n");
        sb.Append("var lb=document.getElementById('lb'),li=document.getElementById('lbimg'),cap=document.getElementById('cap');\n");
        sb.Append("document.querySelectorAll('.grid img').forEach(function(im){im.addEventListener('click',function(){li.src=im.dataset.large;cap.textContent=im.dataset.cap;lb.classList.add('open');});});\n");
        sb.Append("function close(){lb.classList.remove('open');li.src='';}\n");
        sb.Append("document.getElementById('x').addEventListener('click',close);lb.addEventListener('click',function(e){if(e.target===lb)close();});\n");
        sb.Append("document.addEventListener('keydown',function(e){if(e.key==='Escape')close();});\n");
        sb.Append("</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void SaveResized(Image img, string path, int maxEdge, int quality)
    {
        using var clone = img.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgba32>();
        int longEdge = Math.Max(clone.Width, clone.Height);
        if (longEdge > maxEdge)
        {
            double s = (double)maxEdge / longEdge;
            clone.Mutate(x => x.Resize((int)(clone.Width * s), (int)(clone.Height * s)));
        }
        clone.SaveAsJpeg(path, new JpegEncoder { Quality = quality });
    }

    private static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
