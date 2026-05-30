using System;
using System.Collections.Generic;
using System.Linq;
using ImageTool.Core;
using ImageTool.Shared;

namespace ImageTool.Host;

/// <summary>
/// Bộ nhớ tạm "Develop settings" để copy/paste nhanh giữa các ảnh (giống Lightroom
/// Copy/Paste Settings + Sync). Lưu snapshot các EditOperation thuộc plugin "Develop" của
/// ảnh nguồn, rồi áp nguyên trạng sang 1 hay nhiều ảnh đích qua IHistoryService.UpsertGroup.
/// </summary>
public sealed class DevelopClipboard
{
    public const string DevelopPluginId = "Develop";

    private List<EditOperation>? _copied;

    public bool HasData => _copied != null && _copied.Count > 0;

    /// <summary>True nếu đã từng Copy (kể cả copy ảnh gốc rỗng -> paste = reset đích).</summary>
    public bool HasCopied => _copied != null;

    /// <summary>Số op đang giữ (để hiển thị trạng thái).</summary>
    public int Count => _copied?.Count ?? 0;

    /// <summary>Copy các op Develop trong phạm vi active của ảnh nguồn.</summary>
    public bool Copy(IHistoryService history, string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return false;
        var stack = history.GetStack(sourcePath);
        int pointer = history.GetPointer(sourcePath);
        var devOps = stack.Take(pointer)
            .Where(o => string.Equals(o.PluginId, DevelopPluginId, StringComparison.OrdinalIgnoreCase))
            .Select(Clone)
            .ToList();
        _copied = devOps; // có thể rỗng = "ảnh gốc không chỉnh" -> paste sẽ reset đích
        return true;
    }

    /// <summary>Áp settings đã copy sang 1 ảnh đích.</summary>
    public bool PasteTo(IHistoryService history, string targetPath)
    {
        if (_copied == null || string.IsNullOrEmpty(targetPath)) return false;
        // Clone lại để mỗi ảnh có instance Id riêng.
        var ops = _copied.Select(Clone).ToList();
        history.UpsertGroup(targetPath, DevelopPluginId, ops);
        return true;
    }

    /// <summary>Op đang giữ (snapshot read-only) để UI liệt kê module có thể dán.</summary>
    public IReadOnlyList<EditOperation> CopiedOps => _copied ?? (IReadOnlyList<EditOperation>)Array.Empty<EditOperation>();

    /// <summary>
    /// Selective paste (D6.1): chỉ áp các module được chọn từ clipboard sang 1 ảnh đích, GIỮ
    /// nguyên các module khác của đích. Op đích được dựng lại theo thứ tự pipeline chuẩn.
    /// </summary>
    public bool PasteModulesTo(IHistoryService history, string targetPath, ISet<string> moduleKeys)
    {
        if (_copied == null || string.IsNullOrEmpty(targetPath)) return false;
        if (moduleKeys.Count == 0) return false;

        var targetDev = history.GetStack(targetPath)
            .Take(history.GetPointer(targetPath))
            .Where(o => string.Equals(o.PluginId, DevelopPluginId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var merged = DevelopModules.SelectivePaste(targetDev, _copied, moduleKeys)
            .Select(Clone)
            .ToList();
        history.UpsertGroup(targetPath, DevelopPluginId, merged);
        return true;
    }

    /// <summary>Áp 1 số module sang nhiều ảnh (Sync selection theo module).</summary>
    public int PasteModulesToMany(IHistoryService history, IEnumerable<string> targets, ISet<string> moduleKeys)
    {
        if (_copied == null || moduleKeys.Count == 0) return 0;
        int n = 0;
        foreach (var t in targets)
            if (PasteModulesTo(history, t, moduleKeys)) n++;
        return n;
    }

    /// <summary>Module Develop hiện diện trong clipboard (để dựng menu chọn).</summary>
    public IReadOnlyList<DevelopModules.Module> ModulesAvailable()
        => _copied == null ? Array.Empty<DevelopModules.Module>() : DevelopModules.ModulesPresent(_copied);

    /// <summary>Áp sang nhiều ảnh (Sync selection).</summary>
    public int PasteToMany(IHistoryService history, IEnumerable<string> targets)
    {
        if (_copied == null) return 0;
        int n = 0;
        foreach (var t in targets)
            if (PasteTo(history, t)) n++;
        return n;
    }

    private static EditOperation Clone(EditOperation op) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        PluginId = op.PluginId,
        OpType = op.OpType,
        Title = op.Title,
        Timestamp = DateTime.UtcNow,
        Params = new Dictionary<string, string>(op.Params)
    };
}
