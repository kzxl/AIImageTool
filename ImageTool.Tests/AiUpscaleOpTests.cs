using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class AiUpscaleOpTests
{
    private static LinearImage Solid(int w, int h)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = 0.5f; img.Pixels[i + 1] = 0.5f; img.Pixels[i + 2] = 0.5f; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Identity_WhenFactorOne()
    {
        Assert.True(new AiUpscaleOp { Factor = 1 }.IsIdentity);
    }

    [Fact]
    public void NoProcessor_ReturnsSameImage()
    {
        AiOpHost.UpscaleProcessor = null;
        var img = Solid(8, 8);
        var r = new AiUpscaleOp { Factor = 4, PreviewSkip = false }.ApplyResize(img, 1f);
        Assert.Same(img, r); // no-op an toàn
    }

    [Fact]
    public void PreviewSkip_SkipsAtProxyScale()
    {
        bool called = false;
        AiOpHost.UpscaleProcessor = (im, f) => { called = true; return im; };
        try
        {
            var img = Solid(8, 8);
            new AiUpscaleOp { Factor = 4, PreviewSkip = true }.ApplyResize(img, 0.5f);
            Assert.False(called);
            new AiUpscaleOp { Factor = 4, PreviewSkip = true }.ApplyResize(img, 1f);
            Assert.True(called);
        }
        finally { AiOpHost.UpscaleProcessor = null; }
    }

    [Fact]
    public void Processor_UpscalesDimensions()
    {
        AiOpHost.UpscaleProcessor = (im, f) =>
        {
            var big = new LinearImage(im.Width * f, im.Height * f);
            return big;
        };
        try
        {
            var img = Solid(10, 8);
            var r = new AiUpscaleOp { Factor = 2, PreviewSkip = false }.ApplyResize(img, 1f);
            Assert.Equal(20, r.Width);
            Assert.Equal(16, r.Height);
        }
        finally { AiOpHost.UpscaleProcessor = null; }
    }

    [Fact]
    public void RoundTrip_AndRegistered()
    {
        var op = new AiUpscaleOp { Factor = 2, PreviewSkip = false };
        var back = AiUpscaleOp.FromParams(op.ToParams());
        Assert.Equal(2, back.Factor);
        Assert.False(back.PreviewSkip);
        Assert.True(EditOpRegistry.CreateDefault().Has(AiUpscaleOp.Type));
    }
}
