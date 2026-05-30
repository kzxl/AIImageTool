using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageTool.Imaging;

/// <summary>
/// Focus Stacking — ghép nhiều ảnh lấy nét ở các khoảng cách khác nhau thành 1 ảnh NÉT TOÀN BỘ.
/// Đây là cách đúng đắn để có "nét đúng chỗ" khi 1 ảnh đơn không đủ depth-of-field (macro, sản phẩm,
/// phong cảnh cận–viễn). KHÁC với việc "sửa ảnh out nét" — ở đây mỗi vùng được lấy từ ảnh nét nhất.
///
/// Thuật toán: với mỗi ảnh tính focus map (|Laplacian| làm mịn). Mỗi pixel chọn (mềm) từ ảnh có độ
/// nét cao nhất tại đó, trộn theo trọng số softmax để tránh viền cứng. Đầu vào ≥2 ảnh cùng kích thước
/// (giả định đã canh chỉnh/aligned). Thuần thuật toán -> test được.
/// </summary>
public static class FocusStack
{
    public sealed class Options
    {
        public float FocusBlur = 3f;   // làm mịn focus map (giảm nhiễu chọn)
        public float Sharpness = 8f;    // độ "cứng" softmax (cao = chọn dứt khoát ảnh nét nhất)
    }

    public static LinearImage Stack(IReadOnlyList<LinearImage> images, Options? options = null)
    {
        if (images == null || images.Count == 0) throw new ArgumentException("Cần ít nhất 1 ảnh.", nameof(images));
        if (images.Count == 1) return images[0].Clone();

        int w = images[0].Width, h = images[0].Height;
        for (int i = 1; i < images.Count; i++)
            if (images[i].Width != w || images[i].Height != h)
                throw new ArgumentException("Tất cả ảnh phải cùng kích thước.");

        var opt = options ?? new Options();
        int n = images.Count;

        // focus map mỗi ảnh.
        var fmap = new float[n][];
        for (int i = 0; i < n; i++)
        {
            var lap = FocusMeasure.Laplacian(FocusMeasure.ToGray(images[i]), w, h);
            var mag = new float[w * h];
            for (int k = 0; k < mag.Length; k++) mag[k] = MathF.Abs(lap[k]);
            fmap[i] = GaussianBlur.BlurPlane(mag, w, h, MathF.Max(0.5f, opt.FocusBlur));
        }

        var outImg = new LinearImage(w, h);
        float[] o = outImg.Pixels;
        float sharp = MathF.Max(0.1f, opt.Sharpness);
        int total = w * h;

        Parallel.For(0, total, idx =>
        {
            // softmax weight theo focus.
            // chuẩn hoá theo max để ổn định số học.
            float maxF = 0f;
            for (int i = 0; i < n; i++) if (fmap[i][idx] > maxF) maxF = fmap[i][idx];

            float wsum = 0f;
            Span<float> wgt = stackalloc float[16];
            bool useStack = n <= 16;
            float[]? wArr = useStack ? null : new float[n];

            for (int i = 0; i < n; i++)
            {
                float e = MathF.Exp((fmap[i][idx] - maxF) * sharp);
                if (useStack) wgt[i] = e; else wArr![i] = e;
                wsum += e;
            }
            if (wsum < 1e-12f) wsum = 1f;
            float inv = 1f / wsum;

            int p = idx * 4;
            float rr = 0f, gg = 0f, bb = 0f;
            for (int i = 0; i < n; i++)
            {
                float wi = (useStack ? wgt[i] : wArr![i]) * inv;
                float[] px = images[i].Pixels;
                rr += px[p] * wi; gg += px[p + 1] * wi; bb += px[p + 2] * wi;
            }
            o[p] = rr; o[p + 1] = gg; o[p + 2] = bb; o[p + 3] = 1f;
        });
        return outImg;
    }
}
