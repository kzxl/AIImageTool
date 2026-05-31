using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class SmartCropTests
{
    // Tao anh nen toi, dat 1 "vat the" sang (saliency cao) o vung chi dinh.
    private static LinearImage WithBrightBlock(int w, int h, int bx, int by, int bw, int bh)
    {
        var img = new LinearImage(w, h);
        var p = img.Pixels;
        for (int i = 0; i < p.Length; i += 4)
        {
            p[i] = 0.05f; p[i + 1] = 0.05f; p[i + 2] = 0.05f; p[i + 3] = 1f;
        }
        for (int y = by; y < by + bh && y < h; y++)
            for (int x = bx; x < bx + bw && x < w; x++)
            {
                int o = (y * w + x) * 4;
                p[o] = 0.9f; p[o + 1] = 0.9f; p[o + 2] = 0.9f; p[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Best_ReturnsRequestedAspectRatio()
    {
        var img = WithBrightBlock(200, 200, 80, 80, 40, 40);
        var r = SmartCrop.Best(img, 1, 1); // vuong
        // anh vuong, crop 1:1 -> W=H=1 (toan bo).
        Assert.Equal(1f, r.W, 2);
        Assert.Equal(1f, r.H, 2);
    }

    [Fact]
    public void Best_16x9_OnTallImage_HasCorrectAspect()
    {
        var img = WithBrightBlock(400, 800, 150, 100, 100, 100);
        var r = SmartCrop.Best(img, 16, 9);
        // W=1 (gioi han boi chieu rong), H = (9/16) * (400/800)?? -> kiem tra aspect px.
        float cropWpx = r.W * 400, cropHpx = r.H * 800;
        float aspect = cropWpx / cropHpx;
        Assert.InRange(aspect, 16f / 9f - 0.05f, 16f / 9f + 0.05f);
    }

    [Fact]
    public void Best_GravitatesTowardSalientRegion()
    {
        // Vat the sang o NUA TREN cua anh cao -> crop 1:1 nen nam lech len tren (Y nho).
        var img = WithBrightBlock(300, 600, 110, 60, 80, 80);
        var r = SmartCrop.Best(img, 1, 1);
        // crop vuong tren anh 300x600 -> H ~ 0.5, Y nen < 0.25 (lech len tren noi co vat the).
        Assert.True(r.Y < 0.30f, $"Y={r.Y} nen lech len tren noi co saliency");
    }

    [Fact]
    public void Best_SalientBottom_GravitatesDown()
    {
        var img = WithBrightBlock(300, 600, 110, 460, 80, 80);
        var r = SmartCrop.Best(img, 1, 1);
        Assert.True(r.Y > 0.25f, $"Y={r.Y} nen lech xuong duoi noi co saliency");
    }

    [Fact]
    public void Best_EmptyImage_ReturnsFull()
    {
        var img = new LinearImage(1, 1);
        var r = SmartCrop.Best(img, 0, 0);
        Assert.Equal(1f, r.W, 2);
        Assert.Equal(1f, r.H, 2);
    }

    [Fact]
    public void Best_RectStaysInBounds()
    {
        var img = WithBrightBlock(500, 300, 400, 50, 60, 60);
        var r = SmartCrop.Best(img, 3, 2);
        Assert.True(r.X >= 0f && r.Y >= 0f);
        Assert.True(r.X + r.W <= 1.0001f);
        Assert.True(r.Y + r.H <= 1.0001f);
    }
}
