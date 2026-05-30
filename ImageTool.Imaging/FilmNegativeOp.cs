using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageTool.Imaging;

/// <summary>
/// Film Negative (kiểu Darktable "negadoctor") — chuyển ảnh scan PHIM ÂM BẢN thành dương bản đúng cách,
/// khác hẳn InvertOp đơn giản (1 - x). Phim âm bản có:
///  1) "film base" màu cam (D-min) phủ toàn ảnh — phải chia/khử trước khi đảo, nếu không màu sẽ ám cam.
///  2) Quan hệ MẬT ĐỘ (log) chứ không tuyến tính — đảo trong không gian mật độ cho tương phản đúng.
///
/// Mô hình: với mỗi kênh, density = -log10(linear / base). Dương bản = 10^(-(Dmax - density)*gamma)
/// rồi nhân Exposure. Base per-channel (RBase/GBase/BBase) chính là điểm sáng nhất của film base (vùng
/// chưa phơi sáng = tối nhất trên ảnh dương). Gamma điều khiển tương phản, Exposure độ sáng tổng.
///
/// Thuần per-pixel, thuần tham số -> test được. Áp SỚM (sau input profile, trước các op màu khác).
/// </summary>
public sealed class FilmNegativeOp : IEditOp
{
    public const string Type = "FilmNegative";
    public string OpType => Type;

    public bool Enabled;
    // Màu film base (linear) — mặc định hơi cam (R>G>B) như phim màu C-41 điển hình.
    public float RBase = 0.50f;
    public float GBase = 0.30f;
    public float BBase = 0.18f;
    public float Gamma = 1.0f;      // tương phản (0.3..3)
    public float Exposure = 1.0f;   // nhân độ sáng dương bản (0.1..4)

    public bool IsIdentity => !Enabled;

    public void Apply(LinearImage image, float scale)
    {
        if (IsIdentity) return;
        float rb = MathF.Max(1e-4f, RBase);
        float gb = MathF.Max(1e-4f, GBase);
        float bb = MathF.Max(1e-4f, BBase);
        float gamma = Math.Clamp(Gamma, 0.1f, 5f);
        float exp = Math.Clamp(Exposure, 0.01f, 8f);

        image.ProcessPixels((ref float r, ref float g, ref float b, ref float a) =>
        {
            r = ToPositive(r, rb, gamma, exp);
            g = ToPositive(g, gb, gamma, exp);
            b = ToPositive(b, bb, gamma, exp);
        });
    }

    // Chia khử film base rồi đảo trong miền mật độ. ratio = linear/base; dương bản ~ (1/ratio)^gamma.
    private static float ToPositive(float lin, float baseVal, float gamma, float exp)
    {
        float ratio = MathF.Max(1e-5f, lin) / baseVal;   // >1 ở vùng sáng của negative (=tối trên dương)
        // Đảo: dương bản tỉ lệ nghịch; gamma điều khiển tương phản.
        float pos = MathF.Pow(1f / ratio, gamma) * exp;
        if (pos < 0f) pos = 0f;
        return pos;
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["enabled"] = Enabled ? "true" : "false",
        ["rbase"] = F(RBase), ["gbase"] = F(GBase), ["bbase"] = F(BBase),
        ["gamma"] = F(Gamma), ["exposure"] = F(Exposure),
    };
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static FilmNegativeOp FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        Enabled = EditOpRegistry.B(p, "enabled"),
        RBase = EditOpRegistry.F(p, "rbase", 0.50f),
        GBase = EditOpRegistry.F(p, "gbase", 0.30f),
        BBase = EditOpRegistry.F(p, "bbase", 0.18f),
        Gamma = EditOpRegistry.F(p, "gamma", 1f),
        Exposure = EditOpRegistry.F(p, "exposure", 1f),
    };
    public static void Register(EditOpRegistry reg) => reg.Register(Type, FromParams);

    /// <summary>
    /// Lấy mẫu film base từ 1 vùng "trống" của negative (mép phim chưa phơi sáng) — đây là vùng SÁNG
    /// nhất trên scan negative. Trả màu base linear trung bình quanh điểm chuẩn hoá (nx,ny).
    /// </summary>
    public static (float R, float G, float B) SampleBase(LinearImage img, float nx, float ny, int radius = 3)
    {
        int cx = (int)MathF.Round(nx * (img.Width - 1));
        int cy = (int)MathF.Round(ny * (img.Height - 1));
        double sr = 0, sg = 0, sb = 0; int n = 0;
        for (int y = cy - radius; y <= cy + radius; y++)
        {
            if (y < 0 || y >= img.Height) continue;
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || x >= img.Width) continue;
                int o = img.Offset(x, y);
                sr += img.Pixels[o]; sg += img.Pixels[o + 1]; sb += img.Pixels[o + 2]; n++;
            }
        }
        if (n == 0) return (0.5f, 0.3f, 0.18f);
        return ((float)(sr / n), (float)(sg / n), (float)(sb / n));
    }
}
