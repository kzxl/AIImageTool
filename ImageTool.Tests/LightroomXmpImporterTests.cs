using System.Linq;
using ImageTool.Core;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class LightroomXmpImporterTests
{
    private const string Sample = """
        <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
                xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/"
                crs:Exposure2012="+0.75"
                crs:Contrast2012="25"
                crs:Highlights2012="-40"
                crs:Shadows2012="60"
                crs:Vibrance="30"
                crs:Clarity2012="20"
                crs:Dehaze="15"
                crs:ConvertToGrayscale="False"/>
          </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    [Fact]
    public void Parse_ExtractsBasicOp()
    {
        var ops = LightroomXmpImporter.Parse(Sample);
        var basic = ops.FirstOrDefault(o => o.OpType == "DevelopBasic");
        Assert.NotNull(basic);
        Assert.Equal("0.75", basic!.Params["exposure"]);
        Assert.Equal(0.25, double.Parse(basic.Params["contrast"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(-0.40, double.Parse(basic.Params["highlights"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(0.60, double.Parse(basic.Params["shadows"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(0.30, double.Parse(basic.Params["vibrance"], System.Globalization.CultureInfo.InvariantCulture), 3);
    }

    [Fact]
    public void Parse_ExtractsClarityAndDehaze()
    {
        var ops = LightroomXmpImporter.Parse(Sample);
        Assert.Contains(ops, o => o.OpType == "Clarity");
        Assert.Contains(ops, o => o.OpType == "Dehaze");
    }

    [Fact]
    public void Parse_BwFalse_NoBwOp()
    {
        var ops = LightroomXmpImporter.Parse(Sample);
        Assert.DoesNotContain(ops, o => o.OpType == "BlackWhite");
    }

    [Fact]
    public void Parse_BwTrue_AddsBwOp()
    {
        var xmp = Sample.Replace("crs:ConvertToGrayscale=\"False\"", "crs:ConvertToGrayscale=\"True\"");
        var ops = LightroomXmpImporter.Parse(xmp);
        Assert.Contains(ops, o => o.OpType == "BlackWhite");
    }

    [Fact]
    public void Parse_ElementForm_AlsoWorks()
    {
        var xmp = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/">
                  <crs:Exposure2012>-1.0</crs:Exposure2012>
                  <crs:Contrast2012>50</crs:Contrast2012>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(xmp);
        var basic = ops.First(o => o.OpType == "DevelopBasic");
        Assert.Equal("-1", basic.Params["exposure"]);
        Assert.Equal(0.5, double.Parse(basic.Params["contrast"], System.Globalization.CultureInfo.InvariantCulture), 3);
    }

    [Fact]
    public void Parse_AllOps_TaggedAsDevelop()
    {
        var ops = LightroomXmpImporter.Parse(Sample);
        Assert.NotEmpty(ops);
        Assert.All(ops, o => Assert.Equal("Develop", o.PluginId));
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(LightroomXmpImporter.Parse(""));
        Assert.Empty(LightroomXmpImporter.Parse("<x>not xmp</x>"));
    }

    [Fact]
    public void Parse_InvalidXml_NoThrow()
    {
        var ops = LightroomXmpImporter.Parse("<<<broken");
        Assert.Empty(ops);
    }

    [Fact]
    public void Parse_ResultReplaysThroughRegistry()
    {
        // op import được phải dựng lại qua registry (đúng OpType).
        var reg = ImageTool.Imaging.EditOpRegistry.CreateDefault();
        var ops = LightroomXmpImporter.Parse(Sample);
        foreach (var op in ops)
            Assert.True(reg.Has(op.OpType), $"OpType {op.OpType} không đăng ký");
    }

    private const string CurveSample = """
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/">
              <crs:ToneCurvePV2012>
                <rdf:Seq>
                  <rdf:li>0, 0</rdf:li>
                  <rdf:li>64, 40</rdf:li>
                  <rdf:li>192, 216</rdf:li>
                  <rdf:li>255, 255</rdf:li>
                </rdf:Seq>
              </crs:ToneCurvePV2012>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """;

    [Fact]
    public void Parse_ToneCurve_ExtractsNormalizedPoints()
    {
        var ops = LightroomXmpImporter.Parse(CurveSample);
        var curve = ops.FirstOrDefault(o => o.OpType == "ToneCurve");
        Assert.NotNull(curve);
        var rgb = curve!.Params["rgb"];
        // 64/255 ~ 0.251 -> 40/255 ~ 0.157 (điểm tối kéo xuống = S-curve).
        Assert.Contains("0,0", rgb);
        Assert.Contains("1,1", rgb);
        var op = ImageTool.Imaging.ToneCurveOp.FromParams(curve.Params);
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void Parse_LinearToneCurve_Ignored()
    {
        const string linear = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/">
                  <crs:ToneCurvePV2012>
                    <rdf:Seq><rdf:li>0, 0</rdf:li><rdf:li>255, 255</rdf:li></rdf:Seq>
                  </crs:ToneCurvePV2012>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(linear);
        Assert.DoesNotContain(ops, o => o.OpType == "ToneCurve"); // identity -> bỏ
    }

    [Fact]
    public void Parse_PerChannelToneCurve_ExtractsRedChannel()
    {
        const string perCh = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/">
                  <crs:ToneCurvePV2012Red>
                    <rdf:Seq><rdf:li>0, 20</rdf:li><rdf:li>255, 235</rdf:li></rdf:Seq>
                  </crs:ToneCurvePV2012Red>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(perCh);
        var curve = ops.FirstOrDefault(o => o.OpType == "ToneCurve");
        Assert.NotNull(curve);
        Assert.True(curve!.Params.ContainsKey("r"));
        Assert.False(curve.Params.ContainsKey("rgb")); // chỉ có kênh đỏ
        var op = ImageTool.Imaging.ToneCurveOp.FromParams(curve.Params);
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void Parse_SplitToning_ExtractsHueSat()
    {
        const string st = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/"
                    crs:SplitToningShadowHue="220"
                    crs:SplitToningShadowSaturation="40"
                    crs:SplitToningHighlightHue="50"
                    crs:SplitToningHighlightSaturation="30"
                    crs:SplitToningBalance="-20"/>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(st);
        var sp = ops.FirstOrDefault(o => o.OpType == "SplitToning");
        Assert.NotNull(sp);
        Assert.Equal(220, double.Parse(sp!.Params["shHue"], System.Globalization.CultureInfo.InvariantCulture), 1);
        Assert.Equal(0.4, double.Parse(sp.Params["shSat"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(0.3, double.Parse(sp.Params["hiSat"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(-0.2, double.Parse(sp.Params["balance"], System.Globalization.CultureInfo.InvariantCulture), 3);
        var op = ImageTool.Imaging.SplitToningOp.FromParams(sp.Params);
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void Parse_SplitToning_NoSaturation_Ignored()
    {
        const string st = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/"
                    crs:SplitToningShadowHue="220" crs:SplitToningHighlightHue="50"/>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(st);
        Assert.DoesNotContain(ops, o => o.OpType == "SplitToning"); // sat=0 -> bỏ
    }

    [Fact]
    public void Parse_Hsl_ExtractsBandAdjustments()
    {
        const string h = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/"
                    crs:SaturationAdjustmentBlue="-60"
                    crs:LuminanceAdjustmentBlue="-30"
                    crs:HueAdjustmentRed="20"/>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(h);
        var hsl = ops.FirstOrDefault(o => o.OpType == "HslMixer");
        Assert.NotNull(hsl);
        Assert.Equal(-0.6, double.Parse(hsl!.Params["s_blue"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(-0.3, double.Parse(hsl.Params["l_blue"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(0.2, double.Parse(hsl.Params["h_red"], System.Globalization.CultureInfo.InvariantCulture), 3);
        var op = ImageTool.Imaging.HslMixerOp.FromParams(hsl.Params);
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void Parse_Hsl_AllZero_Ignored()
    {
        const string h = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/"
                    crs:SaturationAdjustmentBlue="0" crs:HueAdjustmentRed="0"/>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(h);
        Assert.DoesNotContain(ops, o => o.OpType == "HslMixer");
    }

    [Fact]
    public void Parse_ColorGrading_ExtractsZones()
    {
        const string cg = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/"
                    crs:ColorGradeShadowHue="220" crs:ColorGradeShadowSat="30"
                    crs:ColorGradeHighlightHue="50" crs:ColorGradeHighlightSat="20"
                    crs:ColorGradeBlending="60"/>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(cg);
        var g = ops.FirstOrDefault(o => o.OpType == "ColorGrading");
        Assert.NotNull(g);
        Assert.Equal(220, double.Parse(g!.Params["h_sh"], System.Globalization.CultureInfo.InvariantCulture), 1);
        Assert.Equal(0.3, double.Parse(g.Params["s_sh"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(0.6, double.Parse(g.Params["blend"], System.Globalization.CultureInfo.InvariantCulture), 3);
        var op = ImageTool.Imaging.ColorGradingOp.FromParams(g.Params);
        Assert.False(op.IsIdentity);
    }

    [Fact]
    public void Parse_Texture_Extracts()
    {
        const string t = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:crs="http://ns.adobe.com/camera-raw-settings/1.0/"
                    crs:Texture="45"/>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        var ops = LightroomXmpImporter.Parse(t);
        var tex = ops.FirstOrDefault(o => o.OpType == "Texture");
        Assert.NotNull(tex);
        Assert.Equal(0.45, double.Parse(tex!.Params["amount"], System.Globalization.CultureInfo.InvariantCulture), 3);
    }
}
