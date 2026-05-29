using System;
using System.Globalization;
using System.IO;
using System.Text;
using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Xuất sidecar XMP (.xmp) mô tả các op Develop của 1 ảnh. Mục đích: tương thích/tham chiếu
/// chéo và lưu trữ phi phá hủy theo chuẩn quen thuộc. Đây KHÔNG phải XMP đầy đủ của Lightroom
/// (mapping crs:* phức tạp) mà là namespace riêng "imgtool:" chứa op + params — đủ để app này
/// đọc lại và các công cụ khác xem được metadata.
/// </summary>
public static class XmpSidecar
{
    public static string PathFor(string imagePath)
        => System.IO.Path.ChangeExtension(imagePath, ".xmp");

    /// <summary>Ghi sidecar .xmp cho ảnh từ stack history (chỉ op có Develop pluginId hoặc tất cả).</summary>
    public static void Write(string imagePath, System.Collections.Generic.IReadOnlyList<EditOperation> ops, int pointer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"ImageTool\">");
        sb.AppendLine("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        sb.AppendLine("    <rdf:Description rdf:about=\"\"");
        sb.AppendLine("        xmlns:imgtool=\"http://imagetool/ns/develop/1.0/\">");
        sb.AppendLine($"      <imgtool:Version>1.0</imgtool:Version>");
        sb.AppendLine($"      <imgtool:EditedAt>{DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)}</imgtool:EditedAt>");
        sb.AppendLine("      <imgtool:Operations>");
        sb.AppendLine("        <rdf:Seq>");

        int count = Math.Clamp(pointer, 0, ops.Count);
        for (int i = 0; i < count; i++)
        {
            var op = ops[i];
            sb.AppendLine("          <rdf:li rdf:parseType=\"Resource\">");
            sb.AppendLine($"            <imgtool:PluginId>{Esc(op.PluginId)}</imgtool:PluginId>");
            sb.AppendLine($"            <imgtool:OpType>{Esc(op.OpType)}</imgtool:OpType>");
            foreach (var kv in op.Params)
                sb.AppendLine($"            <imgtool:p_{Esc(kv.Key)}>{Esc(kv.Value)}</imgtool:p_{Esc(kv.Key)}>");
            sb.AppendLine("          </rdf:li>");
        }

        sb.AppendLine("        </rdf:Seq>");
        sb.AppendLine("      </imgtool:Operations>");
        sb.AppendLine("    </rdf:Description>");
        sb.AppendLine("  </rdf:RDF>");
        sb.AppendLine("</x:xmpmeta>");
        sb.AppendLine("<?xpacket end=\"w\"?>");

        var outPath = PathFor(imagePath);
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;
}
