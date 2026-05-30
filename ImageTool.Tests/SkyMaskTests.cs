using System.Collections.Generic;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class SkyMaskTests
{
    // Ảnh: nửa trên xanh trời, nửa dưới xanh lá (đất/cây).
    private static LinearImage SkyOverGround(int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                if (y < h / 2)
                {
                    // trời: xanh lơ sáng (B cao).
                    img.Pixels[o] = ColorSpace.SrgbToLinear(0.45f);
                    img.Pixels[o + 1] = ColorSpace.SrgbToLinear(0.6f);
                    img.Pixels[o + 2] = ColorSpace.SrgbToLinear(0.9f);
                }
                else
                {
                    // đất: xanh lá tối (G nhỉnh, B thấp).
                    img.Pixels[o] = ColorSpace.SrgbToLinear(0.2f);
                    img.Pixels[o + 1] = ColorSpace.SrgbToLinear(0.35f);
                    img.Pixels[o + 2] = ColorSpace.SrgbToLinear(0.15f);
                }
                img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void GenerateFrom_SelectsSkyNotGround()
    {
        var img = SkyOverGround(16, 16);
        var m = new SkyMask { Strength = 0.7f }.GenerateFrom(img);
        // pixel trên (trời) cao hơn pixel dưới (đất).
        int top = 2 * 16 + 8;       // hàng 2
        int bottom = 14 * 16 + 8;   // hàng 14
        Assert.True(m[top] > 0.4f, $"sky {m[top]} phải cao");
        Assert.True(m[bottom] < 0.2f, $"ground {m[bottom]} phải thấp");
    }

    [Fact]
    public void RoundTrip_ViaRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type, ["exposure"] = "0.5",
            ["mask"] = SkyMask.Type, ["strength"] = "0.7", ["smooth"] = "0.15",
        };
        var op = reg.Create(MaskedOp.Type, p);
        Assert.NotNull(op);
        // áp lên ảnh sky/ground: vùng trời sáng lên rõ hơn vùng đất.
        var img = SkyOverGround(16, 16);
        float skyBefore = img.Pixels[(2 * 16 + 8) * 4];
        float groundBefore = img.Pixels[(14 * 16 + 8) * 4];
        op!.Apply(img, 1f);
        float skyDelta = img.Pixels[(2 * 16 + 8) * 4] - skyBefore;
        float groundDelta = img.Pixels[(14 * 16 + 8) * 4] - groundBefore;
        Assert.True(skyDelta > groundDelta);
    }

    [Fact]
    public void Params_RoundTrip()
    {
        var sky = new SkyMask { Strength = 0.5f, Smooth = 0.25f };
        var back = SkyMask.FromParams(sky.ToParams());
        Assert.Equal(0.5f, back.Strength, 4);
        Assert.Equal(0.25f, back.Smooth, 4);
    }
}
