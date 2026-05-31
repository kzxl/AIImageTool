using System;
using System.IO;
using System.Text;
using ImageTool.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
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

    // --- D2.2/7.3: parse colorant matrix (rXYZ/gXYZ/bXYZ) + match space ---

    // White points (XYZ, Y=1) — hằng số chuẩn để dựng dữ liệu test D50.
    private static readonly float[] D50White = { 0.96422f, 1.00000f, 0.82521f };
    private static readonly float[] D65White = { 0.95047f, 1.00000f, 1.08883f };

    // sRGB RGB(linear)->XYZ D65 (hằng số chuẩn, khớp ColorSpaces nội bộ).
    private static readonly float[] SrgbD65 =
    {
        0.4124564f, 0.3575761f, 0.1804375f,
        0.2126729f, 0.7151522f, 0.0721750f,
        0.0193339f, 0.1191920f, 0.9503041f,
    };

    // Dựng ICC có 3 tag colorant rXYZ/gXYZ/bXYZ từ ma trận RGB->XYZ (D50), mỗi tag = type 'XYZ '(4)
    // + reserved(4) + 3 s15Fixed16. 3 cột của ma trận.
    private static byte[] BuildColorants(float[] rgbToXyzD50)
    {
        int tagCount = 3;
        int tableStart = 132;
        int dataStart = tableStart + tagCount * 12;
        int xyzSize = 20; // 4 type + 4 reserved + 12 (3 * s15Fixed16)
        int total = dataStart + tagCount * xyzSize;
        var b = new byte[total];
        b[36] = (byte)'a'; b[37] = (byte)'c'; b[38] = (byte)'s'; b[39] = (byte)'p';
        WriteU32(b, 128, tagCount);

        string[] sigs = { "rXYZ", "gXYZ", "bXYZ" };
        for (int col = 0; col < 3; col++)
        {
            int e = tableStart + col * 12;
            WriteAscii(b, e, sigs[col]);
            int off = dataStart + col * xyzSize;
            WriteU32(b, e + 4, off);
            WriteU32(b, e + 8, xyzSize);
            // tag data
            WriteAscii(b, off, "XYZ ");
            WriteU32(b, off + 4, 0);
            // cột col: X=row0, Y=row1, Z=row2
            WriteS15Fixed16(b, off + 8, rgbToXyzD50[0 * 3 + col]);
            WriteS15Fixed16(b, off + 12, rgbToXyzD50[1 * 3 + col]);
            WriteS15Fixed16(b, off + 16, rgbToXyzD50[2 * 3 + col]);
        }
        return b;
    }

    private static void WriteS15Fixed16(byte[] b, int o, float v)
        => WriteU32(b, o, (int)MathF.Round(v * 65536f));

    [Fact]
    public void ReadRgbToXyz_RoundTripsThroughBradford()
    {
        // D50 colorants = adapt sRGB D65 -> D50; parse phải adapt ngược về ~= D65 gốc.
        float[] toD50 = ColorSpaces.BradfordAdaptation(D65White, D50White);
        float[] srgbD50 = ColorSpaces.Mul3x3(toD50, SrgbD65);

        var icc = BuildColorants(srgbD50);
        var parsed = IccProfileReader.TryReadRgbToXyzD65(icc);
        Assert.NotNull(parsed);
        for (int i = 0; i < 9; i++)
            Assert.InRange(parsed![i] - SrgbD65[i], -1e-3f, 1e-3f);
    }

    [Fact]
    public void MatchSpace_RecognizesKnownGamutsFromMatrix()
    {
        Assert.Equal(ColorSpaces.Space.Srgb, ColorSpaces.MatchSpace(SrgbD65));

        float[] adobeD65 =
        {
            0.5767309f, 0.1855540f, 0.1881852f,
            0.2973769f, 0.6273491f, 0.0752741f,
            0.0270343f, 0.0706872f, 0.9911085f,
        };
        Assert.Equal(ColorSpaces.Space.AdobeRgb, ColorSpaces.MatchSpace(adobeD65));
    }

    [Fact]
    public void MatchSpace_RejectsFarMatrixAndBadInput()
    {
        // Ma trận lệch xa mọi gamut quen thuộc -> null.
        float[] weird = { 0.9f, 0.05f, 0.05f, 0.1f, 0.8f, 0.1f, 0.2f, 0.2f, 0.6f };
        Assert.Null(ColorSpaces.MatchSpace(weird));
        Assert.Null(ColorSpaces.MatchSpace(Array.Empty<float>()));
        Assert.Null(ColorSpaces.MatchSpace(new float[5]));
    }

    [Fact]
    public void EndToEnd_ColorantOnlyProfile_DetectsViaMatrix()
    {
        // Profile không có 'desc' nhận diện được, chỉ có colorant -> phải nhận ra qua ma trận.
        float[] toD50 = ColorSpaces.BradfordAdaptation(D65White, D50White);
        float[] srgbD50 = ColorSpaces.Mul3x3(toD50, SrgbD65);
        var icc = BuildColorants(srgbD50);

        Assert.Null(IccProfileReader.TryReadDescription(icc)); // không có desc tag
        var matched = ColorSpaces.MatchSpace(IccProfileReader.TryReadRgbToXyzD65(icc)!);
        Assert.Equal(ColorSpaces.Space.Srgb, matched);
    }

    [Fact]
    public void ReadRgbToXyz_MissingColorant_ReturnsNull()
    {
        Assert.Null(IccProfileReader.TryReadRgbToXyzD65(BuildV2("sRGB"))); // chỉ có desc, không colorant
        Assert.Null(IccProfileReader.TryReadRgbToXyzD65(null));
        Assert.Null(IccProfileReader.TryReadRgbToXyzD65(new byte[10]));
    }

    // --- End-to-end qua file thật (ImageSharp save/load) ---

    [Fact]
    public void DetectSpaceFromFile_ColorantOnlyIcc_DetectsViaMatrix()
    {
        // Profile chỉ có colorant (không 'desc' nhận diện được) nhúng vào PNG -> detect qua ma trận.
        float[] toD50 = ColorSpaces.BradfordAdaptation(D65White, D50White);
        float[] srgbD50 = ColorSpaces.Mul3x3(toD50, SrgbD65);
        var iccBytes = BuildColorants(srgbD50);

        var dir = Path.Combine(Path.GetTempPath(), "imgtool_icc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "t.png");
            using (var img = new Image<Rgba32>(4, 4, new Rgba32(100, 100, 100, 255)))
            {
                img.Metadata.IccProfile = new IccProfile(iccBytes);
                img.SaveAsPng(path);
            }

            var detected = IccProfileReader.DetectSpaceFromFile(path);
            Assert.Equal(ColorSpaces.Space.Srgb, detected);

            var (desc, space) = IccProfileReader.ReadInfoFromFile(path);
            Assert.Equal(ColorSpaces.Space.Srgb, space);
            // desc có thể null (profile không có tag mô tả) -> chấp nhận.
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DetectSpaceFromFile_NoIcc_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imgtool_icc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "plain.png");
            using (var img = new Image<Rgba32>(4, 4, new Rgba32(50, 60, 70, 255)))
                img.SaveAsPng(path);

            Assert.Null(IccProfileReader.DetectSpaceFromFile(path));
            var (desc, space) = IccProfileReader.ReadInfoFromFile(path);
            Assert.Null(desc);
            Assert.Null(space);
        }
        finally { Directory.Delete(dir, true); }
    }
}
