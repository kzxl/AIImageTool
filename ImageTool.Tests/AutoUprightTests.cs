using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class AutoUprightTests
{
    // Ve 1 duong thang trang tren nen den (Bresenham don gian, do day 2px).
    private static void Line(LinearImage img, float x0, float y0, float x1, float y1)
    {
        int steps = (int)System.MathF.Max(System.MathF.Abs(x1 - x0), System.MathF.Abs(y1 - y0)) * 2 + 1;
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            int px = (int)System.MathF.Round(x0 + (x1 - x0) * t);
            int py = (int)System.MathF.Round(y0 + (y1 - y0) * t);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = px + dx, y = py + dy;
                    if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) continue;
                    int o = (y * img.Width + x) * 4;
                    img.Pixels[o] = 1f; img.Pixels[o + 1] = 1f; img.Pixels[o + 2] = 1f; img.Pixels[o + 3] = 1f;
                }
        }
    }

    private static LinearImage Black(int w, int h) => new LinearImage(w, h);

    [Fact]
    public void ConvergingVerticals_DetectsVerticalLean()
    {
        // Cac duong "dung" hoi tu len dinh (nha chup nguoc) -> co the lean doc.
        var img = Black(256, 256);
        // 4 duong dọc nghiêng hội tụ về tâm trên.
        Line(img, 40, 250, 100, 10);
        Line(img, 100, 250, 120, 10);
        Line(img, 160, 250, 140, 10);
        Line(img, 220, 250, 170, 10);

        var sug = AutoUpright.Estimate(img);
        Assert.True(sug.HasResult);
        // Có lean dọc khác 0 (dấu tuỳ quy ước, chỉ cần phát hiện).
        Assert.True(System.MathF.Abs(sug.Vertical) > 0.02f, $"Vertical={sug.Vertical} nên khác 0");
    }

    [Fact]
    public void StraightVerticals_NearZeroLean()
    {
        // Cac duong dọc thang dung song song -> khong can upright.
        var img = Black(256, 256);
        Line(img, 60, 10, 60, 246);
        Line(img, 120, 10, 120, 246);
        Line(img, 180, 10, 180, 246);

        var sug = AutoUpright.Estimate(img);
        // Hoac khong co ket qua, hoac lean rat nho.
        if (sug.HasResult)
            Assert.True(System.MathF.Abs(sug.Vertical) < 0.15f, $"Vertical={sug.Vertical} nên gần 0 cho ảnh thẳng");
    }

    [Fact]
    public void Blank_NoResult()
    {
        var img = Black(64, 64);
        var sug = AutoUpright.Estimate(img);
        Assert.False(sug.HasResult);
    }

    [Fact]
    public void TooSmall_NoResult()
    {
        var sug = AutoUpright.Estimate(new LinearImage(8, 8));
        Assert.False(sug.HasResult);
    }

    [Fact]
    public void Result_StaysInRange()
    {
        var img = Black(256, 256);
        // Nghiêng mạnh.
        Line(img, 10, 250, 120, 10);
        Line(img, 246, 250, 140, 10);
        var sug = AutoUpright.Estimate(img);
        Assert.InRange(sug.Vertical, -1f, 1f);
        Assert.InRange(sug.Horizontal, -1f, 1f);
    }
}
