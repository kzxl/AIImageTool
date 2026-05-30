using System;
using System.Collections.Generic;
using System.Diagnostics;
using ImageTool.Core;

namespace ImageTool.Imaging;

/// <summary>
/// Đo thời gian từng op khi replay pipeline (#9 — nền tảng cho quyết định GPU compute). Thay vì
/// "đoán" op nào chậm, profiler đo thực tế từng op trên ảnh thật, giúp nhắm tối ưu (CPU SIMD/GPU)
/// đúng chỗ nóng. KHÔNG phải GPU compute — đây là bước đo lường trước khi tối ưu.
///
/// Thuần đo + tổng hợp (logic ghi nhận test được; thời gian tuyệt đối thì không assert).
/// </summary>
public sealed class PipelineProfiler
{
    private readonly EditOpRegistry _ops;

    public PipelineProfiler(EditOpRegistry ops) => _ops = ops;

    public sealed record OpTiming(int Index, string OpType, double Milliseconds);

    public sealed class Report
    {
        public List<OpTiming> Ops { get; } = new();
        public double TotalMs { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>Op chậm nhất (null nếu rỗng).</summary>
        public OpTiming? Slowest()
        {
            OpTiming? max = null;
            foreach (var t in Ops) if (max == null || t.Milliseconds > max.Milliseconds) max = t;
            return max;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Pipeline {Width}x{Height} — {TotalMs:0.0} ms tổng");
            foreach (var t in Ops)
                sb.AppendLine($"  [{t.Index}] {t.OpType}: {t.Milliseconds:0.00} ms");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Replay ops trên 1 clone của baseImage, đo từng op. <paramref name="count"/> &lt;0 = tất cả.
    /// Trả Report với thời gian từng op + tổng.
    /// </summary>
    public Report Profile(LinearImage baseImage, IReadOnlyList<EditOperation> ops, int count = -1)
    {
        var report = new Report();
        var working = baseImage.Clone();
        int n = count < 0 ? ops.Count : Math.Min(count, ops.Count);
        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < n; i++)
        {
            var op = ops[i];
            var impl = _ops.Create(op.OpType, op.Params);
            if (impl == null) continue;

            var sw = Stopwatch.StartNew();
            if (impl is IResizingOp resizing)
                working = resizing.ApplyResize(working, 1f);
            else
                impl.Apply(working, 1f);
            sw.Stop();
            report.Ops.Add(new OpTiming(i, op.OpType, sw.Elapsed.TotalMilliseconds));
        }

        totalSw.Stop();
        report.TotalMs = totalSw.Elapsed.TotalMilliseconds;
        report.Width = working.Width;
        report.Height = working.Height;
        return report;
    }
}
