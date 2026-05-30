using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Cầu nối cho các op AI nặng (denoise/upscale) chạy bằng ONNX ở tầng Host, nhưng vẫn replay được
/// qua pipeline như mọi IEditOp (4.3). Imaging KHÔNG phụ thuộc ONNX: Host đăng ký 1 delegate xử lý;
/// nếu chưa đăng ký (vd môi trường test, chưa tải model) op thành no-op an toàn.
///
/// Delegate nhận (LinearImage, strength, scale) và sửa tại chỗ. Strength [0..1].
/// </summary>
public static class AiOpHost
{
    /// <summary>Bộ xử lý AI denoise do Host cắm vào (ONNX). Null = chưa sẵn sàng -> op no-op.</summary>
    public static Action<LinearImage, float, float>? DenoiseProcessor;

    public static bool HasDenoise => DenoiseProcessor != null;

    /// <summary>
    /// Bộ xử lý AI upscale do Host cắm vào: nhận (ảnh, hệ số phóng) trả ảnh MỚI lớn hơn.
    /// Null = chưa sẵn sàng -> op trả nguyên ảnh.
    /// </summary>
    public static Func<LinearImage, int, LinearImage>? UpscaleProcessor;

    public static bool HasUpscale => UpscaleProcessor != null;
}

/// <summary>
/// Op AI denoise (4.3). Áp ở cuối chuỗi. Thực thi qua <see cref="AiOpHost.DenoiseProcessor"/>:
/// thường chỉ bật ở full-res khi export (model nặng), preview proxy có thể bỏ qua bằng cờ
/// PreviewSkip để kéo slider không lag.
/// </summary>
public sealed class AiDenoiseOp : IEditOp
{
    public const string Type = "AiDenoise";
    public string OpType => Type;

    public float Strength;       // [0..1]
    public bool PreviewSkip = true; // bỏ qua khi scale < 1 (preview) để mượt

    public bool IsIdentity => Strength < 1e-4f;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        if (PreviewSkip && scale < 0.999f) return;       // chỉ chạy full-res
        var proc = AiOpHost.DenoiseProcessor;
        if (proc == null) return;                         // chưa có model -> no-op an toàn
        proc(image, Math.Clamp(Strength, 0f, 1f), scale);
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["strength"] = Strength.ToString("R", CultureInfo.InvariantCulture),
        ["previewSkip"] = PreviewSkip ? "true" : "false",
    };
    public static AiDenoiseOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Strength = EditOpRegistry.F(p, "strength"),
        PreviewSkip = EditOpRegistry.B(p, "previewSkip", true),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);
}
