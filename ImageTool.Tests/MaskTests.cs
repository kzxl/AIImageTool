using System.Collections.Generic;
using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class MaskTests
{
    private static LinearImage Solid(float v, int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void LinearGradient_RangesZeroToOne()
    {
        var m = new LinearGradientMask { X0 = 0f, Y0 = 0f, X1 = 1f, Y1 = 0f }.Generate(16, 16);
        // cột đầu ~0, cột cuối ~1
        Assert.InRange(m[0], 0f, 0.05f);
        Assert.InRange(m[15], 0.95f, 1f);
    }

    [Fact]
    public void RadialMask_CenterDiffersFromEdge()
    {
        var m = new RadialMask { Cx = 0.5f, Cy = 0.5f, Rx = 0.3f, Ry = 0.3f, Feather = 0.4f }.Generate(33, 33);
        int center = 16 * 33 + 16;
        int corner = 0;
        // mặc định hiệu ứng ngoài elip: tâm ~0, góc ~1
        Assert.True(m[center] < m[corner]);
    }

    [Fact]
    public void LuminanceRangeMask_SelectsBrightOnly()
    {
        var img = new LinearImage(2, 1);
        // pixel 0 tối, pixel 1 sáng
        img.Pixels[0] = img.Pixels[1] = img.Pixels[2] = ColorSpace.SrgbToLinear(0.1f); img.Pixels[3] = 1f;
        img.Pixels[4] = img.Pixels[5] = img.Pixels[6] = ColorSpace.SrgbToLinear(0.9f); img.Pixels[7] = 1f;
        var m = new LuminanceRangeMask { Min = 0.7f, Max = 1f, Smooth = 0.05f }.GenerateFrom(img);
        Assert.True(m[0] < 0.1f);  // tối không được chọn
        Assert.True(m[1] > 0.9f);  // sáng được chọn
    }

    [Fact]
    public void MaskedOp_OnlyAffectsMaskedRegion()
    {
        // gradient ngang: trái mask=0 (không đổi), phải mask=1 (áp +1EV).
        var img = Solid(0.25f, 16, 16);
        var inner = new DevelopBasicOp { Exposure = 1f };
        var mask = new LinearGradientMask { X0 = 0f, Y0 = 0f, X1 = 1f, Y1 = 0f };
        var masked = new MaskedOp(inner, mask, null, new Dictionary<string, string>());
        masked.Apply(img, 1f);

        // cột 0 gần như không đổi (~0.25), cột cuối gần x2 (~0.5).
        float left = img.Pixels[0];
        int rightP = (0 * 16 + 15) * 4;
        float right = img.Pixels[rightP];
        Assert.InRange(left, 0.24f, 0.28f);
        Assert.True(right > 0.45f);
    }

    [Fact]
    public void MaskedOp_RoundTripThroughRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(MaskedOp.Type));

        // Dựng params như UI sẽ lưu: inner=DevelopBasic exposure, mask=LinearGradient.
        var p = new Dictionary<string, string>
        {
            ["inner"] = DevelopBasicOp.Type,
            ["exposure"] = "1",
            ["mask"] = LinearGradientMask.Type,
            ["x0"] = "0", ["y0"] = "0", ["x1"] = "1", ["y1"] = "0",
        };
        var op = reg.Create(MaskedOp.Type, p);
        Assert.NotNull(op);

        var img = Solid(0.25f, 16, 16);
        op!.Apply(img, 1f);
        int rightP = (0 * 16 + 15) * 4;
        Assert.True(img.Pixels[rightP] > 0.45f); // phải sáng lên
    }

    [Fact]
    public void MaskedOp_ViaPipeline_Replays()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var baseImg = Solid(0.25f, 16, 16);

        var ops = new List<EditOperation>
        {
            new EditOperation
            {
                OpType = MaskedOp.Type,
                Params = new Dictionary<string, string>
                {
                    ["inner"] = DevelopBasicOp.Type, ["exposure"] = "1",
                    ["mask"] = RadialMask.Type, ["cx"] = "0.5", ["cy"] = "0.5", ["rx"] = "0.3", ["ry"] = "0.3", ["feather"] = "0.5", ["invert"] = "true"
                }
            }
        };
        var result = pipeline.Render(baseImg, ops);
        // base không đổi
        Assert.InRange(baseImg.Pixels[0], 0.249f, 0.251f);
        // tâm (invert=true -> áp trong elip) phải sáng hơn base
        int center = (8 * 16 + 8) * 4;
        Assert.True(result.Pixels[center] > 0.25f);
    }
}
