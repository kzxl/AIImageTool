using System;

namespace ImageTool.Imaging;

// Smart crop (content-aware) lay cam hung tu smartcrop.js: cham diem moi khung crop ung vien
// theo saliency (do tuong phan canh) + skin/mau da + bias trung tam, chon khung diem cao nhat
// cho 1 ti le khung hinh muc tieu. Thuan toan hoc tren LinearImage -> unit test truc tiep.
public static class SmartCrop
{
    public readonly struct Rect
    {
        public readonly float X, Y, W, H; // chuan hoa [0..1]
        public Rect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
    }

    // Tim khung crop tot nhat cho ti le ratioW:ratioH. ratio <= 0 -> giu ti le anh goc.
    // Tra ve khung chuan hoa [0..1] de gan thang vao CropOp.
    public static Rect Best(LinearImage img, double ratioW, double ratioH)
    {
        int w = img.Width, h = img.Height;
        if (w <= 0 || h <= 0) return new Rect(0, 0, 1, 1);

        double targetAspect = (ratioW > 0 && ratioH > 0) ? ratioW / ratioH : (double)w / h;

        // 1) Saliency map o do phan giai thap de nhanh.
        int gw = Math.Min(64, w), gh = Math.Min(64, h);
        float[] sal = Saliency(img, gw, gh);

        // 2) Kich thuoc khung crop (px) lon nhat giu ti le.
        double imgAspect = (double)w / h;
        double cropWpx, cropHpx;
        if (imgAspect > targetAspect) { cropHpx = h; cropWpx = cropHpx * targetAspect; }
        else { cropWpx = w; cropHpx = cropWpx / targetAspect; }
        float cw = (float)(cropWpx / w);   // chuan hoa
        float ch = (float)(cropHpx / h);

        // 3) Quet vi tri khung, cham diem = tong saliency trong khung * bias trung tam.
        float bestScore = -1f;
        float bestX = (1f - cw) * 0.5f, bestY = (1f - ch) * 0.5f;
        const int steps = 24;
        float maxX = 1f - cw, maxY = 1f - ch;
        for (int iy = 0; iy <= steps; iy++)
        {
            float ny = maxY <= 0 ? 0 : maxY * iy / steps;
            for (int ix = 0; ix <= steps; ix++)
            {
                float nx = maxX <= 0 ? 0 : maxX * ix / steps;
                float score = ScoreWindow(sal, gw, gh, nx, ny, cw, ch);
                if (score > bestScore) { bestScore = score; bestX = nx; bestY = ny; }
            }
            if (maxX <= 0 && maxY <= 0) break;
        }

        return new Rect(
            Math.Clamp(bestX, 0f, 1f), Math.Clamp(bestY, 0f, 1f),
            Math.Clamp(cw, 0f, 1f), Math.Clamp(ch, 0f, 1f));
    }

    // Saliency = do lon gradient (Sobel xap xi) tren luminance + cong diem mau da (skin).
    private static float[] Saliency(LinearImage img, int gw, int gh)
    {
        int w = img.Width, h = img.Height;
        float[] px = img.Pixels;

        // Lay mau luminance + skin xuong luoi gw x gh (box sample).
        float[] lum = new float[gw * gh];
        float[] skin = new float[gw * gh];
        for (int gy = 0; gy < gh; gy++)
        {
            int sy = gy * h / gh;
            for (int gx = 0; gx < gw; gx++)
            {
                int sx = gx * w / gw;
                int o = (sy * w + sx) * 4;
                float r = px[o], g = px[o + 1], b = px[o + 2];
                lum[gy * gw + gx] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                skin[gy * gw + gx] = SkinLikelihood(r, g, b);
            }
        }

        float[] sal = new float[gw * gh];
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = y * gw + x;
                int xl = Math.Max(0, x - 1), xr = Math.Min(gw - 1, x + 1);
                int yt = Math.Max(0, y - 1), yb = Math.Min(gh - 1, y + 1);
                float gxv = MathF.Abs(lum[y * gw + xr] - lum[y * gw + xl]);
                float gyv = MathF.Abs(lum[yb * gw + x] - lum[yt * gw + x]);
                float edge = gxv + gyv;
                sal[i] = edge + skin[i] * 0.5f;
            }
        return sal;
    }

    // Uoc luong "mau da" don gian trong linear RGB: R > G > B va do bao hoa vua phai.
    private static float SkinLikelihood(float r, float g, float b)
    {
        if (r <= g || g <= b) return 0f;
        float max = MathF.Max(r, MathF.Max(g, b));
        if (max < 0.06f) return 0f; // qua toi
        float sat = (max - MathF.Min(r, MathF.Min(g, b))) / (max + 1e-6f);
        if (sat < 0.10f || sat > 0.75f) return 0f;
        return 1f;
    }

    private static float ScoreWindow(float[] sal, int gw, int gh, float nx, float ny, float cw, float ch)
    {
        int x0 = (int)MathF.Round(nx * gw);
        int y0 = (int)MathF.Round(ny * gh);
        int x1 = Math.Min(gw, (int)MathF.Round((nx + cw) * gw));
        int y1 = Math.Min(gh, (int)MathF.Round((ny + ch) * gh));
        if (x1 <= x0) x1 = Math.Min(gw, x0 + 1);
        if (y1 <= y0) y1 = Math.Min(gh, y0 + 1);

        // Tam khung trong [0..1] de tinh bias trung tam (uu tien khung gan giua anh nhe).
        float cx = nx + cw * 0.5f, cy = ny + ch * 0.5f;
        float dCenter = MathF.Abs(cx - 0.5f) + MathF.Abs(cy - 0.5f);
        float centerBias = 1f - 0.20f * dCenter;

        float sum = 0f;
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) { sum += sal[y * gw + x]; n++; }
        if (n == 0) return 0f;
        // Diem trung binh saliency * bias -> khong thien khung to.
        return (sum / n) * centerBias;
    }
}
