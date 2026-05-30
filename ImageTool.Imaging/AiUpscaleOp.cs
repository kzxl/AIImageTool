using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// AI Upscale như op chuỗi (#7, 4.3 phần upscale). Là IResizingOp: phóng to ảnh bằng model ONNX
/// (Upscaler) qua <see cref="AiOpHost.UpscaleProcessor"/>. Chỉ chạy full-res khi export (model nặng);
/// preview proxy bỏ qua để mượt. Nếu chưa có processor -> trả nguyên ảnh (no-op an toàn).
///
/// Đặt CUỐI chuỗi để upscale kết quả đã chỉnh sửa. Factor 2/4 (tuỳ model).
/// </summary>
public sealed class AiUpscaleOp : IResizingOp
{
    public const string Type = "AiUpscale";
    public string OpType => Type;

    public int Factor = 4;           // hệ số phóng (model thường 2 hoặc 4)
    public bool PreviewSkip = true;  // bỏ qua ở proxy

    public bool IsIdentity => Factor <= 1;

    public void Apply(LinearImage image, float scale) { /* resizing op */ }

    public LinearImage ApplyResize(LinearImage image, float scale)
    {
        if (IsIdentity) return image;
        if (PreviewSkip && scale < 0.999f) return image;     // chỉ full-res
        var proc = AiOpHost.UpscaleProcessor;
        if (proc == null) return image;                       // chưa có model -> giữ nguyên
        try { return proc(image, Factor) ?? image; }
        catch { return image; }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["factor"] = Factor.ToString(CultureInfo.InvariantCulture),
        ["previewSkip"] = PreviewSkip ? "true" : "false",
    };
    public static AiUpscaleOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Factor = EditOpRegistry.I(p, "factor", 4),
        PreviewSkip = EditOpRegistry.B(p, "previewSkip", true),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
