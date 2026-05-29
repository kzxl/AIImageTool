using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class OpDisplayNamesTests
{
    [Fact]
    public void Get_MapsKnownOpType()
    {
        Assert.Equal("Basic", OpDisplayNames.Get("DevelopBasic"));
        Assert.Equal("Color Grading", OpDisplayNames.Get("ColorGrading"));
        Assert.Equal("Local Adjustment", OpDisplayNames.Get("Masked"));
    }

    [Fact]
    public void Get_PrefersExplicitTitle()
    {
        Assert.Equal("My Custom", OpDisplayNames.Get("DevelopBasic", "My Custom"));
    }

    [Fact]
    public void Get_FallsBackToOpType_WhenUnknown()
    {
        Assert.Equal("SomethingNew", OpDisplayNames.Get("SomethingNew"));
    }

    [Fact]
    public void Get_IgnoresWhitespaceTitle()
    {
        Assert.Equal("Sharpen", OpDisplayNames.Get("Sharpen", "   "));
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        Assert.Equal("Vignette", OpDisplayNames.Get("vignette"));
    }
}
