using ImageTool.Core;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class ColorSpaceTests
{
    [Fact]
    public void SrgbLinearRoundTrip_IsStable()
    {
        for (int i = 0; i <= 100; i++)
        {
            float c = i / 100f;
            float round = ColorSpace.LinearToSrgb(ColorSpace.SrgbToLinear(c));
            Assert.InRange(round, c - 1e-3f, c + 1e-3f);
        }
    }

    [Fact]
    public void EncodeByte_ClampsRange()
    {
        Assert.Equal(0, ColorSpace.EncodeByte(-1f));
        Assert.Equal(255, ColorSpace.EncodeByte(2f));
    }

    [Fact]
    public void Luminance_WhiteIsOne()
    {
        Assert.InRange(ColorSpace.Luminance(1f, 1f, 1f), 0.999f, 1.001f);
    }
}

public class DevelopBasicOpTests
{
    private static LinearImage SolidGray(float v, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f;
        }
        return img;
    }

    [Fact]
    public void Exposure_PlusOneStop_DoublesLinear()
    {
        var img = SolidGray(0.2f);
        new DevelopBasicOp { Exposure = 1f }.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.399f, 0.401f);
    }

    [Fact]
    public void Identity_LeavesPixelsUnchanged()
    {
        var img = SolidGray(0.3f);
        var op = new DevelopBasicOp();
        Assert.True(op.IsIdentity);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.2999f, 0.3001f);
    }

    [Fact]
    public void Saturation_MinusOne_ProducesGray()
    {
        var img = new LinearImage(2, 2);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = 0.8f; img.Pixels[i + 1] = 0.2f; img.Pixels[i + 2] = 0.1f; img.Pixels[i + 3] = 1f;
        }
        new DevelopBasicOp { Saturation = -1f }.Apply(img, 1f);
        // R=G=B sau khi khử bão hoà hoàn toàn.
        Assert.InRange(img.Pixels[0] - img.Pixels[1], -1e-3f, 1e-3f);
        Assert.InRange(img.Pixels[1] - img.Pixels[2], -1e-3f, 1e-3f);
    }

    [Fact]
    public void ParamsRoundTrip_PreservesValues()
    {
        var op = new DevelopBasicOp { Temp = 0.5f, Exposure = -1.25f, Contrast = 0.3f, Vibrance = 0.7f };
        var back = DevelopBasicOp.FromParams(op.ToParams());
        Assert.Equal(op.Temp, back.Temp, 4);
        Assert.Equal(op.Exposure, back.Exposure, 4);
        Assert.Equal(op.Contrast, back.Contrast, 4);
        Assert.Equal(op.Vibrance, back.Vibrance, 4);
    }

    [Fact]
    public void SimdExposurePath_MatchesExpectedAndPreservesAlpha()
    {
        // Ảnh có alpha khác 1 để chắc SIMD không đụng alpha.
        var img = new LinearImage(7, 5); // 35 px, không chia hết cho 8 -> test cả phần dư
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.2f; img.Pixels[i + 1] = 0.3f; img.Pixels[i + 2] = 0.4f; img.Pixels[i + 3] = 0.5f; }

        new DevelopBasicOp { Exposure = 1f }.Apply(img, 1f); // x2

        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            Assert.InRange(img.Pixels[i], 0.399f, 0.401f);     // R x2
            Assert.InRange(img.Pixels[i + 1], 0.599f, 0.601f); // G x2
            Assert.InRange(img.Pixels[i + 2], 0.799f, 0.801f); // B x2
            Assert.InRange(img.Pixels[i + 3], 0.499f, 0.501f); // A giữ nguyên
        }
    }

    [Fact]
    public void SimdWhiteBalancePath_AppliesChannelGains()
    {
        var img = new LinearImage(9, 1); // 9px, dư khi simd=8
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.5f; img.Pixels[i + 1] = 0.5f; img.Pixels[i + 2] = 0.5f; img.Pixels[i + 3] = 1f; }

        new DevelopBasicOp { Temp = 1f }.Apply(img, 1f); // rGain=1.4, bGain=0.6

        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            Assert.InRange(img.Pixels[i], 0.699f, 0.701f);     // R *1.4
            Assert.InRange(img.Pixels[i + 2], 0.299f, 0.301f); // B *0.6
        }
    }

    [Fact]
    public void EncodeByteFast_MatchesEncodeByte()
    {
        for (int i = 0; i <= 1000; i++)
        {
            float lin = i / 1000f;
            int fast = ColorSpace.EncodeByteFast(lin);
            int slow = ColorSpace.EncodeByte(lin);
            Assert.True(Math.Abs(fast - slow) <= 1, $"lin={lin} fast={fast} slow={slow}");
        }
    }
}

public class EditPipelineTests
{
    private static LinearImage SolidGray(float v)
    {
        var img = new LinearImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f;
        }
        return img;
    }

    [Fact]
    public void Render_DoesNotMutateBase()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var baseImg = SolidGray(0.25f);

        var ops = new List<EditOperation>
        {
            new EditOperation { OpType = DevelopBasicOp.Type, Params = new DevelopBasicOp { Exposure = 1f }.ToParams() }
        };

        var result = pipeline.Render(baseImg, ops);
        Assert.InRange(baseImg.Pixels[0], 0.2499f, 0.2501f); // base nguyên vẹn
        Assert.InRange(result.Pixels[0], 0.499f, 0.501f);    // result = x2
    }

    [Fact]
    public void Render_RespectsPointer()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var baseImg = SolidGray(0.25f);

        var ops = new List<EditOperation>
        {
            new EditOperation { OpType = DevelopBasicOp.Type, Params = new DevelopBasicOp { Exposure = 1f }.ToParams() },
            new EditOperation { OpType = DevelopBasicOp.Type, Params = new DevelopBasicOp { Exposure = 1f }.ToParams() }
        };

        // pointer=0 -> base; pointer=1 -> x2; pointer=2 -> x4
        Assert.InRange(pipeline.Render(baseImg, ops, 0).Pixels[0], 0.2499f, 0.2501f);
        Assert.InRange(pipeline.Render(baseImg, ops, 1).Pixels[0], 0.499f, 0.501f);
        Assert.InRange(pipeline.Render(baseImg, ops, 2).Pixels[0], 0.999f, 1.001f);
    }

    [Fact]
    public void UnknownOp_IsSkipped()
    {
        var reg = EditOpRegistry.CreateDefault();
        var pipeline = new EditPipeline(reg);
        var baseImg = SolidGray(0.4f);

        var ops = new List<EditOperation>
        {
            new EditOperation { OpType = "NonExistentOpXyz", Params = new() }
        };

        var result = pipeline.Render(baseImg, ops);
        Assert.InRange(result.Pixels[0], 0.3999f, 0.4001f); // op lạ bị bỏ qua, không lỗi
    }
}

public class HslMixerOpTests
{
    private static LinearImage Solid(float r, float g, float b)
    {
        var img = new LinearImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b; img.Pixels[i + 3] = 1f;
        }
        return img;
    }

    [Fact]
    public void Identity_NoChange()
    {
        var op = new HslMixerOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.6f, 0.2f, 0.2f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.599f, 0.601f);
    }

    [Fact]
    public void GrayPixel_Unaffected()
    {
        var op = new HslMixerOp();
        op.Sat[0] = 1f; // boost red sat
        var img = Solid(0.5f, 0.5f, 0.5f); // xám
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
        Assert.InRange(img.Pixels[1], 0.499f, 0.501f);
    }

    [Fact]
    public void RedSaturationBoost_IncreasesRedChannelSpread()
    {
        var op = new HslMixerOp();
        op.Sat[0] = 0.8f; // red band saturation up
        var img = Solid(0.6f, 0.3f, 0.3f); // đỏ nhạt
        float beforeSpread = 0.6f - 0.3f;
        op.Apply(img, 1f);
        float afterSpread = img.Pixels[0] - img.Pixels[1];
        Assert.True(afterSpread > beforeSpread); // bão hoà tăng -> khoảng cách kênh tăng
    }

    [Fact]
    public void ParamsRoundTrip()
    {
        var op = new HslMixerOp();
        op.Hue[2] = 0.5f; op.Sat[5] = -0.3f; op.Lum[7] = 0.9f;
        var back = HslMixerOp.FromParams(op.ToParams());
        Assert.Equal(0.5f, back.Hue[2], 4);
        Assert.Equal(-0.3f, back.Sat[5], 4);
        Assert.Equal(0.9f, back.Lum[7], 4);
    }
}

public class ToneCurveOpTests
{
    private static LinearImage SolidSrgb(float srgbVal)
    {
        var img = new LinearImage(4, 4);
        float lin = ColorSpace.SrgbToLinear(srgbVal);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = lin; img.Pixels[i + 1] = lin; img.Pixels[i + 2] = lin; img.Pixels[i + 3] = 1f;
        }
        return img;
    }

    [Fact]
    public void Identity_NoChange()
    {
        var op = new ToneCurveOp();
        Assert.True(op.IsIdentity);
        var img = SolidSrgb(0.5f);
        float before = img.Pixels[0];
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], before - 1e-3f, before + 1e-3f);
    }

    [Fact]
    public void LinearCurve_MapsMidpoint()
    {
        // curve kéo điểm giữa lên: (0,0),(0.5,0.75),(1,1)
        var op = new ToneCurveOp(new List<(float, float)> { (0f, 0f), (0.5f, 0.75f), (1f, 1f) });
        Assert.False(op.IsIdentity);
        var img = SolidSrgb(0.5f);
        op.Apply(img, 1f);
        float resultSrgb = ColorSpace.LinearToSrgb(img.Pixels[0]);
        Assert.InRange(resultSrgb, 0.70f, 0.80f); // ~0.75
    }

    [Fact]
    public void ParamsRoundTrip()
    {
        var op = new ToneCurveOp(new List<(float, float)> { (0f, 0.1f), (1f, 0.9f) });
        var back = ToneCurveOp.FromParams(op.ToParams());
        var imgA = SolidSrgb(0.5f); op.Apply(imgA, 1f);
        var imgB = SolidSrgb(0.5f); back.Apply(imgB, 1f);
        Assert.InRange(imgB.Pixels[0], imgA.Pixels[0] - 1e-3f, imgA.Pixels[0] + 1e-3f);
    }
}

public class SpatialOpsTests
{
    private static LinearImage Checker(int w = 16, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float v = ((x + y) % 2 == 0) ? 0.3f : 0.6f;
                img.Pixels[o] = v; img.Pixels[o + 1] = v; img.Pixels[o + 2] = v; img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Clarity_Identity_NoChange()
    {
        var op = new ClarityOp { Amount = 0f };
        Assert.True(op.IsIdentity);
        var img = Checker();
        float before = img.Pixels[0];
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], before - 1e-4f, before + 1e-4f);
    }

    [Fact]
    public void Sharpen_IncreasesLocalContrast()
    {
        var img = Checker();
        // pixel (0,0)=0.3 cạnh các pixel 0.6 -> sharpen kéo nó xuống (xa trung bình).
        float before = img.Pixels[0];
        new SharpenOp { Amount = 0.8f, Radius = 1f }.Apply(img, 1f);
        Assert.True(img.Pixels[0] <= before + 1e-4f);
    }

    [Fact]
    public void Vignette_DarkensCorners_KeepsCenter()
    {
        var img = new LinearImage(33, 33);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.8f; img.Pixels[i + 1] = 0.8f; img.Pixels[i + 2] = 0.8f; img.Pixels[i + 3] = 1f; }

        new VignetteOp { Amount = -0.8f, Midpoint = 0.3f, Feather = 0.7f }.Apply(img, 1f);

        int center = (16 * 33 + 16) * 4;
        int corner = 0;
        Assert.InRange(img.Pixels[center], 0.79f, 0.81f); // tâm gần như nguyên
        Assert.True(img.Pixels[corner] < 0.7f);           // góc bị tối
    }

    [Fact]
    public void Sharpen_ParamsRoundTrip()
    {
        var op = new SharpenOp { Amount = 0.5f, Radius = 1.5f, Threshold = 0.2f, Masking = 0.4f };
        var back = SharpenOp.FromParams(op.ToParams());
        Assert.Equal(0.5f, back.Amount, 4);
        Assert.Equal(1.5f, back.Radius, 4);
        Assert.Equal(0.2f, back.Threshold, 4);
        Assert.Equal(0.4f, back.Masking, 4);
    }

    // Ảnh nửa trái phẳng (0.5), nửa phải có 1 cạnh dọc mạnh giữa cột giữa.
    private static LinearImage HalfFlatHalfEdge(int w = 32, int h = 16)
    {
        var img = new LinearImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float v;
                if (x < w / 2) v = 0.5f;                  // nửa trái: phẳng
                else v = (x < 3 * w / 4) ? 0.2f : 0.8f;   // nửa phải: cạnh bậc thang
                img.Pixels[o] = v; img.Pixels[o + 1] = v; img.Pixels[o + 2] = v; img.Pixels[o + 3] = 1f;
            }
        return img;
    }

    [Fact]
    public void Sharpen_Masking_ProtectsFlatRegions()
    {
        // Thêm 1 chấm nhiễu nhỏ ở vùng phẳng để xem masking có ghìm sharpen không.
        var img = HalfFlatHalfEdge();
        int w = img.Width, h = img.Height;
        int flatX = 8, flatY = 8;
        int o = (flatY * w + flatX) * 4;
        img.Pixels[o] = 0.55f; img.Pixels[o + 1] = 0.55f; img.Pixels[o + 2] = 0.55f; // nhiễu nhẹ

        var noMask = img.Clone();
        var withMask = img.Clone();
        new SharpenOp { Amount = 0.9f, Radius = 1f, Masking = 0f }.Apply(noMask, 1f);
        new SharpenOp { Amount = 0.9f, Radius = 1f, Masking = 1f }.Apply(withMask, 1f);

        // Tại vùng phẳng, masking=1 phải ít thay đổi hơn masking=0 (gần giá trị gốc hơn).
        float orig = 0.55f;
        float devNo = MathF.Abs(noMask.Pixels[o] - orig);
        float devMask = MathF.Abs(withMask.Pixels[o] - orig);
        Assert.True(devMask < devNo, $"masking phải ghìm sharpen ở vùng phẳng: devMask={devMask} devNo={devNo}");
    }

    [Fact]
    public void Sharpen_Masking_StillSharpensStrongEdges()
    {
        var img = HalfFlatHalfEdge();
        int w = img.Width;
        // pixel ngay sát cạnh mạnh (cột 3w/4) ở vùng tối.
        int ex = 3 * w / 4 - 1, ey = 8;
        int o = (ey * w + ex) * 4;
        float before = img.Pixels[o];
        new SharpenOp { Amount = 0.9f, Radius = 1f, Masking = 0.8f }.Apply(img, 1f);
        // cạnh mạnh vẫn được sharpen -> pixel tối sát cạnh bị kéo tối hơn (xa trung bình).
        Assert.NotEqual(before, img.Pixels[o], 3);
    }
}

public class ColorGradingGrainTests
{
    private static LinearImage Solid(float v, int w = 8, int h = 8)
    {
        var img = new LinearImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = v; img.Pixels[i + 1] = v; img.Pixels[i + 2] = v; img.Pixels[i + 3] = 1f; }
        return img;
    }

    [Fact]
    public void ColorGrading_Identity_NoChange()
    {
        var op = new ColorGradingOp();
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void ColorGrading_ShadowTint_ShiftsColor()
    {
        var op = new ColorGradingOp();
        op.Hue[0] = 30f; op.Sat[0] = 1f; // shadows warm
        var img = Solid(0.15f); // tối -> thuộc shadows
        op.Apply(img, 1f);
        // R nên >= B sau khi thêm sắc ấm.
        Assert.True(img.Pixels[0] >= img.Pixels[2]);
    }

    [Fact]
    public void ColorGrading_ParamsRoundTrip()
    {
        var op = new ColorGradingOp { Blending = 0.7f };
        op.Hue[2] = 210f; op.Sat[2] = 0.6f; op.Lum[0] = -0.4f;
        var back = ColorGradingOp.FromParams(op.ToParams());
        Assert.Equal(210f, back.Hue[2], 2);
        Assert.Equal(0.6f, back.Sat[2], 4);
        Assert.Equal(-0.4f, back.Lum[0], 4);
        Assert.Equal(0.7f, back.Blending, 4);
    }

    [Fact]
    public void Grain_Identity_NoChange()
    {
        var op = new GrainOp { Amount = 0f };
        Assert.True(op.IsIdentity);
        var img = Solid(0.5f);
        op.Apply(img, 1f);
        Assert.InRange(img.Pixels[0], 0.499f, 0.501f);
    }

    [Fact]
    public void Grain_IsDeterministic()
    {
        var a = Solid(0.5f); new GrainOp { Amount = 0.8f, Seed = 42 }.Apply(a, 1f);
        var b = Solid(0.5f); new GrainOp { Amount = 0.8f, Seed = 42 }.Apply(b, 1f);
        for (int i = 0; i < a.Pixels.Length; i++)
            Assert.Equal(a.Pixels[i], b.Pixels[i], 5);
    }

    [Fact]
    public void Grain_AltersMidtone()
    {
        var img = Solid(0.5f);
        new GrainOp { Amount = 1f, Seed = 7 }.Apply(img, 1f);
        bool anyChanged = false;
        for (int i = 0; i < img.Pixels.Length; i += 4)
            if (MathF.Abs(img.Pixels[i] - 0.5f) > 1e-4f) { anyChanged = true; break; }
        Assert.True(anyChanged);
    }
}

public class AutoToneTests
{
    [Fact]
    public void DarkImage_SuggestsPositiveExposure()
    {
        // Ảnh tối đều -> auto nên đề xuất tăng exposure.
        var img = new LinearImage(16, 16);
        float dark = ColorSpace.SrgbToLinear(0.15f);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = dark; img.Pixels[i + 1] = dark; img.Pixels[i + 2] = dark; img.Pixels[i + 3] = 1f; }

        var s = AutoTone.Analyze(img);
        Assert.True(s.Exposure > 0f);
    }

    [Fact]
    public void BrightImage_SuggestsNegativeExposure()
    {
        var img = new LinearImage(16, 16);
        float bright = ColorSpace.SrgbToLinear(0.85f);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = bright; img.Pixels[i + 1] = bright; img.Pixels[i + 2] = bright; img.Pixels[i + 3] = 1f; }

        var s = AutoTone.Analyze(img);
        Assert.True(s.Exposure < 0f);
    }

    [Fact]
    public void AutoTone_AppliedViaDevelopBasic_BrightensDarkImage()
    {
        var img = new LinearImage(16, 16);
        float dark = ColorSpace.SrgbToLinear(0.15f);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = dark; img.Pixels[i + 1] = dark; img.Pixels[i + 2] = dark; img.Pixels[i + 3] = 1f; }

        var s = AutoTone.Analyze(img);
        var op = new DevelopBasicOp
        {
            Exposure = s.Exposure, Contrast = s.Contrast, Whites = s.Whites,
            Blacks = s.Blacks, Shadows = s.Shadows, Highlights = s.Highlights
        };
        float before = img.Pixels[0];
        op.Apply(img, 1f);
        Assert.True(img.Pixels[0] > before); // sáng hơn
    }
}
