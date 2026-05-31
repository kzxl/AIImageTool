using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class ExportSizeEstimatorTests
{
    [Fact]
    public void Estimate_ZeroDimensions_Zero()
    {
        Assert.Equal(0, ExportSizeEstimator.EstimateBytes("jpg", 0, 0, 0, 90));
    }

    [Fact]
    public void Estimate_HigherQuality_LargerFile()
    {
        long q50 = ExportSizeEstimator.EstimateBytes("jpg", 4000, 3000, 0, 50);
        long q90 = ExportSizeEstimator.EstimateBytes("jpg", 4000, 3000, 0, 90);
        Assert.True(q90 > q50);
    }

    [Fact]
    public void Estimate_Resize_ReducesSize()
    {
        long full = ExportSizeEstimator.EstimateBytes("jpg", 4000, 3000, 0, 90);
        long small = ExportSizeEstimator.EstimateBytes("jpg", 4000, 3000, 1024, 90);
        Assert.True(small < full);
    }

    [Fact]
    public void Estimate_Png_LargerThanJpeg()
    {
        long jpg = ExportSizeEstimator.EstimateBytes("jpg", 2000, 2000, 0, 85);
        long png = ExportSizeEstimator.EstimateBytes("png", 2000, 2000, 0, 85);
        Assert.True(png > jpg);
    }

    [Fact]
    public void Estimate_Webp_SmallerThanJpeg()
    {
        long jpg = ExportSizeEstimator.EstimateBytes("jpg", 2000, 2000, 0, 85);
        long webp = ExportSizeEstimator.EstimateBytes("webp", 2000, 2000, 0, 85);
        Assert.True(webp < jpg);
    }

    [Fact]
    public void Estimate_Tiff_Largest()
    {
        long png = ExportSizeEstimator.EstimateBytes("png", 1000, 1000, 0, 90);
        long tiff = ExportSizeEstimator.EstimateBytes("tiff", 1000, 1000, 0, 90);
        Assert.True(tiff > png);
        // TIFF không nén ~ 1000*1000*3 byte.
        Assert.InRange(tiff, 2_900_000, 3_100_000);
    }

    [Fact]
    public void Estimate_NoUpscale_WhenSmallerThanMax()
    {
        // ảnh 800px, maxLong 2048 -> không phóng to, dùng kích thước gốc.
        long a = ExportSizeEstimator.EstimateBytes("jpg", 800, 600, 2048, 90);
        long b = ExportSizeEstimator.EstimateBytes("jpg", 800, 600, 0, 90);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(3_145_728L, "3 MB")]
    public void Format_HumanReadable(long bytes, string expected)
    {
        Assert.Equal(expected, ExportSizeEstimator.Format(bytes));
    }
}
