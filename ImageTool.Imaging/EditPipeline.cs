using System;
using System.Collections.Generic;
using ImageTool.Core;

namespace ImageTool.Imaging;

/// <summary>
/// Bộ render phi phá hủy. Nhận ảnh GỐC bất biến + chuỗi EditOperation (từ IHistoryService)
/// và dựng lại kết quả bằng cách replay từng op trên 1 bản clone của gốc.
///
/// Nhờ vậy: undo/redo = đổi pointer rồi render lại; đổi tham số 1 op cũ = sửa Params rồi
/// render lại — không bao giờ tích luỹ sai số như edit phá hủy kiểu Mutate() cũ.
/// </summary>
public sealed class EditPipeline
{
    private readonly EditOpRegistry _ops;

    public EditPipeline(EditOpRegistry ops) => _ops = ops;

    /// <summary>
    /// Render full-res. <paramref name="baseImage"/> là ảnh gốc (linear) — KHÔNG bị sửa.
    /// <paramref name="ops"/> theo thứ tự; chỉ áp tới <paramref name="count"/> op đầu (pointer của history).
    /// count &lt; 0 nghĩa là áp toàn bộ.
    /// </summary>
    public LinearImage Render(LinearImage baseImage, IReadOnlyList<EditOperation> ops, int count = -1)
        => RenderScaled(baseImage, ops, 1f, count);

    /// <summary>
    /// Render trên ảnh đã thu nhỏ sẵn (proxy) cho preview thời gian thực.
    /// <paramref name="proxyBase"/> là bản đã resize của gốc; <paramref name="scale"/> = proxyW/fullW.
    /// Op tự nhân bán kính theo scale để hiệu ứng khớp với full-res.
    /// </summary>
    public LinearImage RenderScaled(LinearImage proxyBase, IReadOnlyList<EditOperation> ops, float scale, int count = -1)
    {
        var working = proxyBase.Clone();
        int n = count < 0 ? ops.Count : Math.Min(count, ops.Count);
        for (int i = 0; i < n; i++)
        {
            var op = ops[i];
            var impl = _ops.Create(op.OpType, op.Params);
            if (impl == null) continue; // op lạ (chưa đăng ký) bị bỏ qua, không phá replay
            if (impl is IResizingOp resizing)
                working = resizing.ApplyResize(working, scale); // đổi kích thước -> thay ảnh working
            else
                impl.Apply(working, scale);
        }
        return working;
    }
}
