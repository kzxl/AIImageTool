using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class HighlightReconstructionOpTests
{
    private static LinearImage Solid(float r, float g, float b)
    {
        var img = new LinearImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void Identity_WhenZeroAmount()
    {
        Assert.True(new HighlightReconstructionOp { Amount = 0 }.IsIdentity);
    }

    [Fact]
    public void Registered()
    {
        Assert.True(EditOpRegistry.CreateDefault().Has(HighlightReconstructionOp.Type));
    }

    [Fact]
    public void RoundTrip()
    {
        var back = HighlightReconstructionOp.FromParams(
            new HighlightReconstructionOp { Amount = 0.7f, Threshold = 0.9f }.ToParams());
        Assert.Equal(0.7f, back.Amount, 4);
        Assert.Equal(0.9f, back.Threshold, 4);
    }

    [Fact]
    public void NeutralizesColorCastInBrightHighlight()
    {
        // highlight ám hồng: R cao (cháy), G/B thấp hơn. Recon kéo G/B lên gần R -> bớt ám.
        var img = Solid(1.0f, 0.7f, 0.75f);
        float gapBefore = img.Pixels[0] - img.Pixels[1]; // R - G
        new HighlightReconstructionOp { Amount = 1f, Threshold = 0.8f }.Apply(img, 1f);
        float gapAfter = img.Pixels[0] - img.Pixels[1];
        Assert.True(gapAfter < gapBefore, $"recon should reduce channel gap: {gapBefore} -> {gapAfter}");
    }

    [Fact]
    public void LeavesMidtonesUnchanged()
    {
        // pixel tối/midtone dưới ngưỡng -> không đổi.
        var img = Solid(0.4f, 0.3f, 0.35f);
        var before = (float[])img.Pixels.Clone();
        new HighlightReconstructionOp { Amount = 1f, Threshold = 0.85f }.Apply(img, 1f);
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], img.Pixels[i], 5);
    }

    [Fact]
    public void PreservesBrightness()
    {
        // max-channel (độ sáng đỉnh) giữ nguyên sau recon.
        var img = Solid(1.0f, 0.6f, 0.7f);
        float maxBefore = System.MathF.Max(img.Pixels[0], System.MathF.Max(img.Pixels[1], img.Pixels[2]));
        new HighlightReconstructionOp { Amount = 1f, Threshold = 0.8f }.Apply(img, 1f);
        float maxAfter = System.MathF.Max(img.Pixels[0], System.MathF.Max(img.Pixels[1], img.Pixels[2]));
        Assert.Equal(maxBefore, maxAfter, 3);
    }
}
