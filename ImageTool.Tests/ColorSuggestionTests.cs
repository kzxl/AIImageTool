using System.Collections.Generic;
using System.Linq;
using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class ColorSuggestionTests
{
    [Fact]
    public void FromDominant_ReturnsFiveHarmonies()
    {
        var s = ColorSuggestion.FromDominant(200, 50, 50); // đỏ
        Assert.Equal(5, s.Count);
        Assert.Contains(s, x => x.Role == "Bổ túc");
        Assert.Contains(s, x => x.Role == "Triadic A");
    }

    [Fact]
    public void FromDominant_ComplementOfRed_IsCyanish()
    {
        // bổ túc của đỏ (~hue 0) là cyan (~hue 180): B và G cao hơn R.
        var s = ColorSuggestion.FromDominant(220, 40, 40);
        var comp = s.First(x => x.Role == "Bổ túc");
        Assert.True(comp.G > comp.R || comp.B > comp.R);
    }

    [Fact]
    public void FromDominant_HexValid()
    {
        var s = ColorSuggestion.FromDominant(100, 150, 200);
        foreach (var x in s)
        {
            Assert.StartsWith("#", x.Hex);
            Assert.Equal(7, x.Hex.Length);
        }
    }

    [Fact]
    public void AssessContrast_FlatMonochrome_LowScore()
    {
        var sw = new List<(byte, byte, byte)>
        {
            ((byte)100, (byte)100, (byte)100),
            ((byte)110, (byte)110, (byte)110),
        };
        var (score, advice) = ColorSuggestion.AssessContrast(sw);
        Assert.True(score < 0.3);
        Assert.Contains("phẳng", advice);
    }

    [Fact]
    public void AssessContrast_HighDynamicRange_HigherScore()
    {
        var flat = new List<(byte, byte, byte)>
        {
            ((byte)120, (byte)120, (byte)120), ((byte)130, (byte)130, (byte)130)
        };
        var contrasty = new List<(byte, byte, byte)>
        {
            ((byte)10, (byte)10, (byte)10),       // tối
            ((byte)245, (byte)245, (byte)245),    // sáng
            ((byte)230, (byte)40, (byte)40),      // đỏ
            ((byte)40, (byte)80, (byte)230),      // xanh
        };
        var s1 = ColorSuggestion.AssessContrast(flat).Score;
        var s2 = ColorSuggestion.AssessContrast(contrasty).Score;
        Assert.True(s2 > s1);
    }

    [Fact]
    public void AssessContrast_TooFew_ReturnsZero()
    {
        var (score, _) = ColorSuggestion.AssessContrast(new List<(byte, byte, byte)> { ((byte)1, (byte)2, (byte)3) });
        Assert.Equal(0, score);
    }
}
