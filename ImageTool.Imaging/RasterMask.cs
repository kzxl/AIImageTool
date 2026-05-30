using System;
using System.Collections.Generic;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageTool.Imaging;

/// <summary>
/// Mask raster "nướng sẵn" (baked) — dùng cho AI mask (Subject/Sky/Background, 6.6). Khác các mask
/// hình học (gradient/radial) sinh bằng công thức, mask này là 1 ảnh xám đã tính sẵn (vd output
/// segmentation ONNX), lưu thành PNG trong cache và tham chiếu qua đường dẫn. Khi áp, nạp PNG rồi
/// nội suy song tuyến về đúng kích thước ảnh hiện tại (proxy hay full-res đều khớp).
///
/// Serialize: "mask"=Raster, "maskFile"=đường dẫn PNG xám, "invert"=true/false.
/// Giữ Imaging không phụ thuộc ONNX: AI chạy ở Host, chỉ ghi PNG rồi tạo mask này.
/// </summary>
public sealed class RasterMask : IMaskGenerator
{
    public const string Type = "Raster";
    public string MaskType => Type;

    public string MaskFile = "";
    public bool Invert;

    // Cache mask đã nạp (theo file) để khỏi đọc PNG mỗi lần render proxy + full-res.
    private float[]? _srcMask;
    private int _srcW, _srcH;

    public float[] Generate(int width, int height)
    {
        EnsureLoaded();
        var m = new float[width * height];
        if (_srcMask == null || _srcW == 0 || _srcH == 0)
        {
            // không nạp được -> mask rỗng (không áp gì), an toàn.
            return m;
        }

        // Nội suy song tuyến từ (srcW×srcH) về (width×height).
        for (int y = 0; y < height; y++)
        {
            float sy = height <= 1 ? 0 : y / (float)(height - 1) * (_srcH - 1);
            int y0 = (int)sy; int y1 = Math.Min(_srcH - 1, y0 + 1);
            float ty = sy - y0;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float sx = width <= 1 ? 0 : x / (float)(width - 1) * (_srcW - 1);
                int x0 = (int)sx; int x1 = Math.Min(_srcW - 1, x0 + 1);
                float tx = sx - x0;
                float v00 = _srcMask[y0 * _srcW + x0];
                float v10 = _srcMask[y0 * _srcW + x1];
                float v01 = _srcMask[y1 * _srcW + x0];
                float v11 = _srcMask[y1 * _srcW + x1];
                float top = v00 + (v10 - v00) * tx;
                float bot = v01 + (v11 - v01) * tx;
                float v = top + (bot - top) * ty;
                m[row + x] = Invert ? 1f - v : v;
            }
        }
        return m;
    }

    private void EnsureLoaded()
    {
        if (_srcMask != null) return;
        if (string.IsNullOrEmpty(MaskFile) || !System.IO.File.Exists(MaskFile)) return;
        try
        {
            using var img = Image.Load<L8>(MaskFile);
            _srcW = img.Width; _srcH = img.Height;
            var buf = new float[_srcW * _srcH];
            img.ProcessPixelRows(acc =>
            {
                for (int y = 0; y < acc.Height; y++)
                {
                    var rowSpan = acc.GetRowSpan(y);
                    int o = y * _srcW;
                    for (int x = 0; x < rowSpan.Length; x++)
                        buf[o + x] = rowSpan[x].PackedValue / 255f;
                }
            });
            _srcMask = buf;
        }
        catch { _srcMask = null; }
    }

    public Dictionary<string, string> ToParams() => new()
    {
        ["mask"] = Type, ["maskFile"] = MaskFile, ["invert"] = Invert ? "true" : "false",
    };

    public static RasterMask FromParams(IReadOnlyDictionary<string, string> p) => new()
    {
        MaskFile = EditOpRegistry.S(p, "maskFile"),
        Invert = EditOpRegistry.B(p, "invert"),
    };
}
