using System;
using System.Text;
using ImageTool.Imaging;
using Xunit;

namespace ImageTool.Tests;

public class IccProfileReaderTests
{
    // Dựng 1 ICC tối thiểu hợp lệ: header 128 byte (có 'acsp' @36) + 1 tag 'desc'.
    private static byte[] BuildV2(string desc)
    {
        var name = Encoding.ASCII.GetBytes(desc);
        // desc tag: 'desc'(4) + reserved(4) + count(4) + string(count, gồm NUL).
        int count = name.Length + 1;
        int tagSize = 12 + count;
        int tagOffset = 128 + 4 + 12; // header + tagCount + 1 entry

        int total = tagOffset + tagSize;
        var b = new byte[total];
        // 'acsp'
        b[36] = (byte)'a'; b[37] = (byte)'c'; b[38] = (byte)'s'; b[39] = (byte)'p';
        // tag count = 1
        WriteU32(b, 128, 1);
        // tag entry: sig 'desc', offset, size
        WriteAscii(b, 132, "desc");
        WriteU32(b, 136, tagOffset);
        WriteU32(b, 140, tagSize);
        // tag data
        WriteAscii(b, tagOffset, "desc");
        WriteU32(b, tagOffset + 4, 0);       // reserved
        WriteU32(b, tagOffset + 8, count);   // ASCII count
        Array.Copy(name, 0, b, tagOffset + 12, name.Length); // string (NUL tự để 0)
        return b;
    }

    // Dựng ICC v4 với tag 'desc' kiểu mluc (UTF-16BE).
    private static byte[] BuildV4Mluc(string desc)
    {
        var str = Encoding.BigEndianUnicode.GetBytes(desc);
        // mluc: type(4)+reserved(4)+numRec(4)+recSize(4)+ [lang(2)+country(2)+len(4)+off(4)] + string
        int recSize = 12;
        int strOffInTag = 16 + recSize;
        int tagSize = strOffInTag + str.Length;
        int tagOffset = 128 + 4 + 12;
        int total = tagOffset + tagSize;
        var b = new byte[total];
        b[36] = (byte)'a'; b[37] = (byte)'c'; b[38] = (byte)'s'; b[39] = (byte)'p';
        WriteU32(b, 128, 1);
        WriteAscii(b, 132, "desc");
        WriteU32(b, 136, tagOffset);
        WriteU32(b, 140, tagSize);
        // mluc data
        WriteAscii(b, tagOffset, "mluc");
        WriteU32(b, tagOffset + 4, 0);
        WriteU32(b, tagOffset + 8, 1);          // numRecords
        WriteU32(b, tagOffset + 12, recSize);   // recordSize
        b[tagOffset + 16] = (byte)'e'; b[tagOffset + 17] = (byte)'n';
        b[tagOffset + 18] = (byte)'U'; b[tagOffset + 19] = (byte)'S';
        WriteU32(b, tagOffset + 20, str.Length);    // length
        WriteU32(b, tagOffset + 24, strOffInTag);   // offset from tag start
        Array.Copy(str, 0, b, tagOffset + strOffInTag, str.Length);
        return b;
    }

    private static void WriteU32(byte[] b, int o, int v)
    { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    private static void WriteAscii(byte[] b, int o, string s)
    { var a = Encoding.ASCII.GetBytes(s); Array.Copy(a, 0, b, o, a.Length); }

    [Fact]
    public void ReadDescription_V2_Ascii()
    {
        var icc = BuildV2("Adobe RGB (1998)");
        Assert.Equal("Adobe RGB (1998)", IccProfileReader.TryReadDescription(icc));
    }

    [Fact]
    public void ReadDescription_V4_Mluc()
    {
        var icc = BuildV4Mluc("Display P3");
        Assert.Equal("Display P3", IccProfileReader.TryReadDescription(icc));
    }

    [Fact]
    public void ReadDescription_NotIcc_ReturnsNull()
    {
        Assert.Null(IccProfileReader.TryReadDescription(new byte[200])); // không có 'acsp'
        Assert.Null(IccProfileReader.TryReadDescription(null));
        Assert.Null(IccProfileReader.TryReadDescription(new byte[10])); // quá ngắn
    }

    [Theory]
    [InlineData("Adobe RGB (1998)", "AdobeRGB")]
    [InlineData("Display P3", "DisplayP3")]
    [InlineData("Rec. 2020", "Rec2020")]
    [InlineData("sRGB IEC61966-2.1", "sRGB")]
    public void GuessSpace_MapsKnownProfiles(string desc, string expected)
    {
        var sp = IccProfileReader.GuessSpace(desc);
        Assert.NotNull(sp);
        Assert.Equal(expected, ColorSpaces.Name(sp!.Value));
    }

    [Fact]
    public void GuessSpace_Unknown_ReturnsNull()
    {
        Assert.Null(IccProfileReader.GuessSpace("Some Camera Profile XYZ"));
        Assert.Null(IccProfileReader.GuessSpace(""));
        Assert.Null(IccProfileReader.GuessSpace(null));
    }

    [Fact]
    public void EndToEnd_V2AdobeRgb_GuessesAdobe()
    {
        var icc = BuildV2("Adobe RGB (1998)");
        var desc = IccProfileReader.TryReadDescription(icc);
        var sp = IccProfileReader.GuessSpace(desc);
        Assert.Equal(ColorSpaces.Space.AdobeRgb, sp);
    }
}
