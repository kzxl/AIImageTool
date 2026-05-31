using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

// Xác nhận logic khoá cache thumbnail (10.9): đổi mtime/dung lượng/size -> khoá đổi (tự sinh lại).
public class ThumbnailCacheKeyTests
{
    [Fact]
    public void SameInputs_SameKey()
    {
        var a = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 1000, 2048, 256);
        var b = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 1000, 2048, 256);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PathCaseInsensitive_SameKey()
    {
        var a = ThumbnailService.ComposeCacheKey(@"C:\P\IMG.JPG", 1000, 2048, 256);
        var b = ThumbnailService.ComposeCacheKey(@"c:\p\img.jpg", 1000, 2048, 256);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentMtime_DifferentKey()
    {
        var a = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 1000, 2048, 256);
        var b = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 2000, 2048, 256);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentLength_DifferentKey()
    {
        var a = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 1000, 2048, 256);
        var b = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 1000, 4096, 256);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DifferentSize_DifferentKey()
    {
        var a = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 1000, 2048, 128);
        var b = ThumbnailService.ComposeCacheKey(@"C:\p\img.jpg", 1000, 2048, 256);
        Assert.NotEqual(a, b);
    }
}
