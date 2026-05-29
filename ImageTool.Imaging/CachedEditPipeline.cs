using System;
using System.Collections.Generic;
using System.Text;
using ImageTool.Core;

namespace ImageTool.Imaging;

/// <summary>
/// Pipeline có CACHE THEO TẦNG (10.6). Lưu ảnh trung gian sau từng op; khi render lại chỉ
/// replay TỪ op đầu tiên bị đổi trở đi (longest-common-prefix theo "chữ ký" op), tái dùng
/// snapshot cho phần đầu không đổi.
///
/// Tình huống tối ưu: kéo 1 slider chỉ đổi op cuối -> replay đúng 1 op trên snapshot trước đó,
/// thay vì replay cả chuỗi. Kết quả PHẢI trùng khít <see cref="EditPipeline"/>.
///
/// Bộ nhớ có giới hạn: chỉ cache snapshot cho tối đa <see cref="MaxCheckpoints"/> op đầu;
/// op sâu hơn vẫn replay nhưng không lưu snapshot. Cache bị huỷ khi đổi ảnh nền hoặc scale.
/// Không thread-safe — mỗi renderer giữ 1 instance, gọi tuần tự.
/// </summary>
public sealed class CachedEditPipeline
{
    private readonly EditOpRegistry _ops;

    /// <summary>Số snapshot tối đa giữ lại (mỗi snapshot ~ 1 bản clone proxy).</summary>
    public int MaxCheckpoints { get; set; } = 16;

    private string? _cacheKey;
    private float _cacheScale = float.NaN;
    private readonly List<string> _sigs = new();       // chữ ký op tại mỗi bậc đã cache
    private readonly List<LinearImage> _after = new();  // ảnh SAU khi áp op i (bản clone bất biến)

    public CachedEditPipeline(EditOpRegistry ops) => _ops = ops;

    /// <summary>Số snapshot đang giữ (cho test/giám sát).</summary>
    public int CachedDepth => _after.Count;

    /// <summary>Xoá toàn bộ cache (gọi khi đổi ảnh để giải phóng RAM).</summary>
    public void Invalidate()
    {
        _cacheKey = null;
        _cacheScale = float.NaN;
        _sigs.Clear();
        _after.Clear();
    }

    /// <summary>
    /// Render proxy với cache theo tầng. <paramref name="cacheKey"/> định danh ảnh nền (vd path).
    /// Trả ảnh kết quả (bản mới, an toàn cho caller sửa tiếp).
    /// </summary>
    public LinearImage RenderScaled(string cacheKey, LinearImage proxyBase, IReadOnlyList<EditOperation> ops, float scale, int count = -1)
    {
        int n = count < 0 ? ops.Count : Math.Min(count, ops.Count);

        // Huỷ cache nếu ảnh nền hoặc scale đổi.
        if (!string.Equals(_cacheKey, cacheKey, StringComparison.Ordinal) || !Eq(_cacheScale, scale))
        {
            _cacheKey = cacheKey;
            _cacheScale = scale;
            _sigs.Clear();
            _after.Clear();
        }

        // Longest common prefix giữa op hiện tại và sig đã cache (bị giới hạn bởi số snapshot có).
        int cachedDepth = _after.Count;
        int lcp = 0;
        while (lcp < n && lcp < cachedDepth && string.Equals(_sigs[lcp], Sig(ops[lcp]), StringComparison.Ordinal))
            lcp++;

        // Ảnh khởi điểm: snapshot sau op (lcp-1), hoặc clone ảnh nền nếu lcp==0.
        LinearImage working = lcp > 0 ? _after[lcp - 1].Clone() : proxyBase.Clone();

        // Cắt bỏ cache từ lcp trở đi (đã không còn hợp lệ).
        if (_sigs.Count > lcp) _sigs.RemoveRange(lcp, _sigs.Count - lcp);
        if (_after.Count > lcp) _after.RemoveRange(lcp, _after.Count - lcp);

        // Replay op [lcp..n).
        for (int i = lcp; i < n; i++)
        {
            var op = ops[i];
            var impl = _ops.Create(op.OpType, op.Params);
            if (impl != null)
            {
                if (impl is IResizingOp resizing)
                    working = resizing.ApplyResize(working, scale);
                else
                    impl.Apply(working, scale);
            }
            // op lạ (impl==null) bị bỏ qua như EditPipeline, nhưng vẫn ghi checkpoint để giữ thẳng index.

            // Ghi snapshot cho op i nếu còn trong ngân sách.
            if (i < MaxCheckpoints)
            {
                _sigs.Add(Sig(op));
                _after.Add(working.Clone());
            }
        }

        return working;
    }

    /// <summary>Chữ ký ổn định của 1 op: OpType + Params (key sắp xếp). Quyết định cache hit.</summary>
    private static string Sig(EditOperation op)
    {
        var sb = new StringBuilder(op.OpType);
        sb.Append('#');
        var keys = new List<string>(op.Params.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var k in keys)
        {
            sb.Append(k).Append('=').Append(op.Params[k]).Append(';');
        }
        return sb.ToString();
    }

    private static bool Eq(float a, float b)
        => (float.IsNaN(a) && float.IsNaN(b)) || MathF.Abs(a - b) < 1e-9f;
}
