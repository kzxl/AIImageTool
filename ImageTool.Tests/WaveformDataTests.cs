using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class WaveformDataTests
{
    private static LinearImage SolidSrgb(float srgb, int w = 16, int h = 8)
    {
        var img = new LinearImage(w, h);
        float lin = ColorSpace.SrgbToLinear(srgb);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = lin; img.Pixels[i + 1] = lin; img.Pixels[i + 2] = lin; img.Pixels[i + 3] = 1f; }
        return img;
    }

    // Ảnh nửa trái đen, nửa phải trắng (theo cột x).
    private static LinearImage LeftBlackRightWhite(int w = 16, int h = 8)
    {
        var img = new LinearImage(w, h);
        float white = ColorSpace.SrgbToLinear(1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = img.Offset(x, y);
                float v = x < w / 2 ? 0f : white;
                img.Pixels[o] = v; img.Pixels[o + 1] = v; img.Pixels[o + 2] = v; img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Compute_ColumnsClampedToWidth()
    {
        var wf = WaveformData.Compute(SolidSrgb(0.5f, 4, 4), columns: 256);
        Assert.Equal(4, wf.Columns); // không vượt width
    }

    [Fact]
    public void Compute_MidGray_AllInOneLumaBin()
    {
        var img = SolidSrgb(0.5f, 16, 8);
        var wf = WaveformData.Compute(img, columns: 16);
        // mỗi cột 8 pixel cùng mức -> tổng theo cột = 8, tập trung 1 bin.
        int total = 0;
        for (int v = 0; v < 256; v++) total += wf.Luma[0, v];
        Assert.Equal(8, total);
    }

    [Fact]
    public void Compute_LeftBlackRightWhite_SeparatesByColumn()
    {
        var wf = WaveformData.Compute(LeftBlackRightWhite(16, 8), columns: 16);
        // Cột trái: luma ở bin 0 (đen). Cột phải: luma ở bin 255 (trắng).
        Assert.True(wf.Luma[0, 0] > 0);
        Assert.Equal(0, wf.Luma[0, 255]);
        Assert.True(wf.Luma[15, 255] > 0);
        Assert.Equal(0, wf.Luma[15, 0]);
    }

    [Fact]
    public void Compute_RgbParade_RedColumnHasRedHigh()
    {
        // Ảnh đỏ thuần: kênh R ở 255, G/B ở 0.
        var img = new LinearImage(8, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 1f; img.Pixels[i + 1] = 0f; img.Pixels[i + 2] = 0f; img.Pixels[i + 3] = 1f; }
        var wf = WaveformData.Compute(img, columns: 8);
        Assert.True(wf.R[0, 255] > 0);
        Assert.True(wf.G[0, 0] > 0);
        Assert.True(wf.B[0, 0] > 0);
        Assert.Equal(0, wf.R[0, 0]);
    }

    [Fact]
    public void Compute_MaxCount_Positive()
    {
        var wf = WaveformData.Compute(SolidSrgb(0.5f, 16, 16), columns: 16);
        Assert.True(wf.MaxCount >= 16); // mỗi cột 16 pixel cùng mức
    }

    [Fact]
    public void Compute_ZeroColumns_ClampedToOne()
    {
        var wf = WaveformData.Compute(SolidSrgb(0.5f, 8, 8), columns: 0);
        Assert.True(wf.Columns >= 1);
    }
}
