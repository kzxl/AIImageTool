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
}
