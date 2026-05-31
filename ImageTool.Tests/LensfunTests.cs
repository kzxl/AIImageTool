using System;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class LensfunTests
{
    private const string Sample = """
        <lensdatabase>
          <lens>
            <maker>Canon</maker>
            <model>Canon EF 50mm f/1.8 STM</model>
            <mount>Canon EF</mount>
            <cropfactor>1.0</cropfactor>
            <calibration>
              <distortion model="poly3" focal="50" k1="0.012"/>
              <vignetting model="pa" focal="50" aperture="1.8" distance="1000" k1="-0.5" k2="0.3" k3="-0.1"/>
            </calibration>
          </lens>
          <lens>
            <maker>Canon</maker>
            <model>Canon EF 24-70mm f/2.8L</model>
            <mount>Canon EF</mount>
            <cropfactor>1.0</cropfactor>
            <calibration>
              <distortion model="poly5" focal="24" k1="-0.08" k2="0.02"/>
              <distortion model="poly5" focal="70" k1="0.01" k2="0.005"/>
            </calibration>
          </lens>
        </lensdatabase>
        """;

    [Fact]
    public void Parse_ReadsLenses()
    {
        var db = LensfunDatabase.ParseXml(Sample);
        Assert.Equal(2, db.Lenses.Count);
        var prime = db.FindLens("Canon EF 50mm f/1.8 STM");
        Assert.NotNull(prime);
        Assert.Single(prime!.Distortions);
        Assert.Equal("poly3", prime.Distortions[0].Model);
        Assert.Equal(0.012f, prime.Distortions[0].K1, 4);
        Assert.Single(prime.Vignettings);
        Assert.Equal(-0.5f, prime.Vignettings[0].K1, 4);
    }

    [Fact]
    public void FindLens_ExactAndContains()
    {
        var db = LensfunDatabase.ParseXml(Sample);
        // Exact (chuẩn hoá hoa thường/space).
        Assert.NotNull(db.FindLens("canon ef 50mm f/1.8 stm"));
        // EXIF thường chỉ ghi model ngắn -> khớp chứa nhau.
        Assert.NotNull(db.FindLens("EF 24-70mm f/2.8L"));
        Assert.Null(db.FindLens("Nikon 85mm"));
        Assert.Null(db.FindLens(""));
    }

    [Fact]
    public void InterpolateDistortion_BetweenFocals()
    {
        var db = LensfunDatabase.ParseXml(Sample);
        var zoom = db.FindLens("Canon EF 24-70mm f/2.8L")!;
        // Ở 24mm -> k1 = -0.08; ở 70mm -> k1 = 0.01. Tại tiêu cự giữa, k1 nằm giữa 2 giá trị.
        var mid = LensfunDatabase.InterpolateDistortion(zoom, 40f);
        Assert.NotNull(mid);
        Assert.Equal("poly5", mid!.Model);
        Assert.InRange(mid.K1, -0.08f, 0.01f);
    }

    [Fact]
    public void InterpolateDistortion_ClampsOutOfRange()
    {
        var db = LensfunDatabase.ParseXml(Sample);
        var zoom = db.FindLens("Canon EF 24-70mm f/2.8L")!;
        var below = LensfunDatabase.InterpolateDistortion(zoom, 10f);  // < 24
        var above = LensfunDatabase.InterpolateDistortion(zoom, 200f); // > 70
        Assert.Equal(-0.08f, below!.K1, 4);
        Assert.Equal(0.01f, above!.K1, 4);
    }

    [Fact]
    public void InterpolateDistortion_SingleEntry()
    {
        var db = LensfunDatabase.ParseXml(Sample);
        var prime = db.FindLens("Canon EF 50mm f/1.8 STM")!;
        var d = LensfunDatabase.InterpolateDistortion(prime, 50f);
        Assert.Equal(0.012f, d!.K1, 4);
    }

    [Fact]
    public void Parse_BadXml_NoThrow()
    {
        var db = LensfunDatabase.ParseXml("<<<broken", "");
        Assert.Empty(db.Lenses);
    }

    // ---- LensProfileOp math ----

    [Fact]
    public void Op_Identity_WhenNoCoeffs()
    {
        Assert.True(new LensProfileOp().IsIdentity);
    }

    [Fact]
    public void DistortionFactor_Poly3()
    {
        var op = new LensProfileOp { DistModel = "poly3", Dk1 = 0.1f };
        // factor(0) = 1; factor(1) = 1 + 0.1.
        Assert.Equal(1f, op.DistortionFactor(0f), 4);
        Assert.Equal(1.1f, op.DistortionFactor(1f), 4);
    }

    [Fact]
    public void DistortionFactor_Poly5()
    {
        var op = new LensProfileOp { DistModel = "poly5", Dk1 = 0.1f, Dk2 = 0.05f };
        // factor(1) = 1 + 0.1 + 0.05.
        Assert.Equal(1.15f, op.DistortionFactor(1f), 4);
    }

    [Fact]
    public void VignetteIntensity_PaModel()
    {
        var op = new LensProfileOp { Vk1 = -0.5f, Vk2 = 0.3f, Vk3 = -0.1f };
        // tại tâm r=0 -> intensity = 1 (không tối).
        Assert.Equal(1f, op.VignetteIntensity(0f), 4);
        // tại r=1 -> 1 - 0.5 + 0.3 - 0.1 = 0.7 (góc tối hơn).
        Assert.Equal(0.7f, op.VignetteIntensity(1f), 4);
    }

    [Fact]
    public void Op_FromCalib_BuildsCorrectly()
    {
        var dist = new LensfunDatabase.DistortionCalib { Model = "poly3", K1 = 0.02f };
        var vig = new LensfunDatabase.VignettingCalib { K1 = -0.4f };
        var op = LensProfileOp.FromCalib(dist, vig);
        Assert.Equal("poly3", op.DistModel);
        Assert.Equal(0.02f, op.Dk1, 4);
        Assert.Equal(-0.4f, op.Vk1, 4);
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void Op_RoundTripParams()
    {
        var op = new LensProfileOp { DistModel = "ptlens", Dk1 = 0.01f, Dk2 = 0.02f, Dk3 = 0.03f, Vk1 = -0.3f };
        var back = LensProfileOp.FromParams(op.ToParams());
        Assert.Equal("ptlens", back.DistModel);
        Assert.Equal(0.01f, back.Dk1, 4);
        Assert.Equal(0.03f, back.Dk3, 4);
        Assert.Equal(-0.3f, back.Vk1, 4);
    }

    [Fact]
    public void Op_VignetteCorrection_BrightensCorner()
    {
        // Ảnh xám đều; vignetting k1<0 (góc tối) -> bù phải làm góc SÁNG hơn tâm.
        var img = new LinearImage(33, 33);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        { img.Pixels[i] = 0.5f; img.Pixels[i + 1] = 0.5f; img.Pixels[i + 2] = 0.5f; img.Pixels[i + 3] = 1f; }
        new LensProfileOp { Vk1 = -0.5f, CorrectDistortion = false }.Apply(img, 1f);
        int center = (16 * 33 + 16) * 4;
        int corner = 0;
        Assert.True(img.Pixels[corner] > img.Pixels[center]);
    }

    [Fact]
    public void Op_RegisteredInRegistry()
    {
        var reg = EditOpRegistry.CreateDefault();
        Assert.True(reg.Has(LensProfileOp.Type));
    }

    // ---- LensfunService bridge ----

    [Fact]
    public void Service_BuildOpFor_MatchedLens()
    {
        var db = LensfunDatabase.ParseXml(Sample);
        var svc = new ImageTool.Shared.LensfunService(db);
        Assert.True(svc.HasDatabase);

        var op = svc.BuildOpFor("Canon EF 50mm f/1.8 STM", 50f);
        Assert.NotNull(op);
        Assert.Equal("poly3", op!.DistModel);
        Assert.Equal(0.012f, op.Dk1, 4);
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void Service_BuildOpFor_UnknownLens_Null()
    {
        var svc = new ImageTool.Shared.LensfunService(LensfunDatabase.ParseXml(Sample));
        Assert.Null(svc.BuildOpFor("Nikon 85mm f/1.4", 85f));
        Assert.Null(svc.BuildOpFor(null, 50f));
    }

    [Fact]
    public void Service_MatchLensName()
    {
        var svc = new ImageTool.Shared.LensfunService(LensfunDatabase.ParseXml(Sample));
        Assert.Equal("Canon EF 24-70mm f/2.8L", svc.MatchLensName("EF 24-70mm f/2.8L"));
        Assert.Null(svc.MatchLensName("Sigma 35mm"));
    }

    [Fact]
    public void Service_NoDatabase_BuildsNull()
    {
        var svc = new ImageTool.Shared.LensfunService(new LensfunDatabase());
        Assert.False(svc.HasDatabase);
        Assert.Null(svc.BuildOpFor("Canon EF 50mm f/1.8 STM", 50f));
    }
}
