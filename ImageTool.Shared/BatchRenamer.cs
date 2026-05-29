using System;
using System.Collections.Generic;
using System.IO;

namespace ImageTool.Shared;

/// <summary>
/// Thực thi đổi tên hàng loạt an toàn (13.7) dựa trên kế hoạch của <see cref="FileNameTokenizer"/>.
/// An toàn: đổi tên qua tên tạm trung gian trước (2 pha) để tránh đụng độ khi tập tên mới giao
/// với tên cũ (vd hoán đổi a&lt;-&gt;b). Bỏ qua mục mà tên không đổi. Trả kết quả từng mục.
/// Thuần IO + logic -> test được bằng thư mục tạm.
/// </summary>
public static class BatchRenamer
{
    public sealed class Result
    {
        public string OldPath { get; init; } = "";
        public string NewPath { get; init; } = "";
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Đổi tên các file theo plan (oldPath -> newFileName). newFileName chỉ là TÊN (không path);
    /// file mới nằm cùng thư mục với file cũ. Dùng pha tạm để tránh va chạm.
    /// </summary>
    public static List<Result> Execute(IReadOnlyList<(string OldPath, string NewName)> plan)
    {
        var results = new List<Result>(plan.Count);
        var temps = new List<(string Temp, string Final, string Old)>();

        // Pha 1: đổi sang tên tạm duy nhất (chỉ những mục thực sự đổi).
        foreach (var (oldPath, newName) in plan)
        {
            try
            {
                var dir = Path.GetDirectoryName(oldPath) ?? ".";
                var finalPath = Path.Combine(dir, newName);
                if (string.Equals(oldPath, finalPath, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new Result { OldPath = oldPath, NewPath = oldPath, Success = true });
                    continue;
                }
                if (!File.Exists(oldPath))
                {
                    results.Add(new Result { OldPath = oldPath, NewPath = finalPath, Success = false, Error = "File không tồn tại" });
                    continue;
                }
                var temp = Path.Combine(dir, ".rntmp_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + newName);
                File.Move(oldPath, temp);
                temps.Add((temp, finalPath, oldPath));
            }
            catch (Exception ex)
            {
                results.Add(new Result { OldPath = oldPath, NewPath = oldPath, Success = false, Error = ex.Message });
            }
        }

        // Pha 2: tạm -> tên cuối.
        foreach (var (temp, final, old) in temps)
        {
            try
            {
                if (File.Exists(final))
                {
                    // không ghi đè: phục hồi tên cũ.
                    File.Move(temp, old);
                    results.Add(new Result { OldPath = old, NewPath = final, Success = false, Error = "Tên đích đã tồn tại" });
                    continue;
                }
                File.Move(temp, final);
                results.Add(new Result { OldPath = old, NewPath = final, Success = true });
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Move(temp, old); } catch { }
                results.Add(new Result { OldPath = old, NewPath = final, Success = false, Error = ex.Message });
            }
        }

        return results;
    }
}
