using System;

namespace ImageTool.Imaging;

/// <summary>
/// Auto White Balance (13.2). Ước lượng và đề xuất gain để trung hoà ám màu (color cast), dùng
/// 2 chiến lược cổ điển:
///   - GrayWorld: giả định trung bình cảnh là xám -> gain = avgGray/avgChannel.
///   - WhitePatch: giả định điểm sáng nhất là trắng -> gain = max/channelMax.
/// Trả gain per-channel (chuẩn hoá theo G để giữ độ sáng). Áp bằng cách nhân linear RGB
/// (tương đương op gain trắng). Bỏ qua pixel quá tối/quá sáng để tránh nhiễu.
/// </summary>
public static class AutoWhiteBalance
{
    public enum Strategy { GrayWorld, WhitePatch }

    public struct Gains
    {
        public float R, G, B;
        public bool IsNeutral => MathF.Abs(R - 1f) < 1e-3f && MathF.Abs(G - 1f) < 1e-3f && MathF.Abs(B - 1f) < 1e-3f;
    }

    public static Gains Analyze(LinearImage img, Strategy strategy = Strategy.GrayWorld)
    {
        float[] px = img.Pixels;
        int n = img.PixelCount;

        if (strategy == Strategy.GrayWorld)
        {
            double sr = 0, sg = 0, sb = 0; long cnt = 0;
            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                float r = px[o], g = px[o + 1], b = px[o + 2];
                float lum = ColorSpace.Luminance(r, g, b);
                // bỏ pixel rất tối/rất sáng (ít tin cậy cho WB).
                if (lum < 0.02f || lum > 0.9f) continue;
                sr += r; sg += g; sb += b; cnt++;
            }
            if (cnt == 0) return Neutral();
            double avgR = sr / cnt, avgG = sg / cnt, avgB = sb / cnt;
            double avgGray = (avgR + avgG + avgB) / 3.0;
            return Normalize(
                avgR > 1e-6 ? (float)(avgGray / avgR) : 1f,
                avgG > 1e-6 ? (float)(avgGray / avgG) : 1f,
                avgB > 1e-6 ? (float)(avgGray / avgB) : 1f);
        }
        else // WhitePatch
        {
            float mr = 0, mg = 0, mb = 0;
            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                if (px[o] > mr) mr = px[o];
                if (px[o + 1] > mg) mg = px[o + 1];
                if (px[o + 2] > mb) mb = px[o + 2];
            }
            float mx = MathF.Max(mr, MathF.Max(mg, mb));
            if (mx < 1e-6f) return Neutral();
            return Normalize(
                mr > 1e-6f ? mx / mr : 1f,
                mg > 1e-6f ? mx / mg : 1f,
                mb > 1e-6f ? mx / mb : 1f);
        }
    }

    private static Gains Neutral() => new() { R = 1f, G = 1f, B = 1f };

    // Chuẩn hoá theo G để giữ độ sáng tương đối (G gain = 1).
    private static Gains Normalize(float r, float g, float b)
    {
        if (g < 1e-6f) g = 1f;
        return new Gains { R = r / g, G = 1f, B = b / g };
    }
}
