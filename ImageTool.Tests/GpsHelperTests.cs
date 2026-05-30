using ImageTool.Shared;
using Xunit;

namespace ImageTool.Tests;

public class GpsHelperTests
{
    [Fact]
    public void ToDecimalDegrees_North_Positive()
    {
        // 21° 1' 42.64" N ~ 21.028511
        var dd = GpsHelper.ToDecimalDegrees(21, 1, 42.64, "N");
        Assert.NotNull(dd);
        Assert.Equal(21.0285, dd!.Value, 4);
    }

    [Fact]
    public void ToDecimalDegrees_South_Negative()
    {
        var dd = GpsHelper.ToDecimalDegrees(33, 51, 54, "S");
        Assert.NotNull(dd);
        Assert.True(dd!.Value < 0);
    }

    [Fact]
    public void ToDecimalDegrees_West_Negative()
    {
        var dd = GpsHelper.ToDecimalDegrees(118, 14, 37, "W");
        Assert.NotNull(dd);
        Assert.True(dd!.Value < 0);
    }

    [Fact]
    public void ToDecimalDegrees_OutOfRange_Null()
    {
        Assert.Null(GpsHelper.ToDecimalDegrees(200, 0, 0, "N"));
    }

    [Fact]
    public void IsValid_RejectsZeroIsland()
    {
        Assert.False(GpsHelper.IsValid(0, 0));
        Assert.True(GpsHelper.IsValid(21.0285, 105.8048));
        Assert.False(GpsHelper.IsValid(91, 0));
        Assert.False(GpsHelper.IsValid(0, 181));
    }

    [Fact]
    public void GoogleMapsUrl_ContainsCoords()
    {
        var url = GpsHelper.GoogleMapsUrl(21.028511, 105.804817);
        Assert.Contains("21.028511", url);
        Assert.Contains("105.804817", url);
        Assert.StartsWith("https://www.google.com/maps", url);
    }

    [Fact]
    public void Format_SixDecimals()
    {
        Assert.Equal("21.028511, 105.804817", GpsHelper.Format(21.028511, 105.804817));
    }

    [Fact]
    public void ParseDms_Rational()
    {
        var dms = GpsHelper.ParseDms("21/1 1/1 4264/100");
        Assert.NotNull(dms);
        Assert.Equal(21, dms!.Value.D, 3);
        Assert.Equal(1, dms.Value.M, 3);
        Assert.Equal(42.64, dms.Value.S, 2);
    }

    [Fact]
    public void ParseDms_Invalid_Null()
    {
        Assert.Null(GpsHelper.ParseDms("21 1"));
        Assert.Null(GpsHelper.ParseDms(""));
    }
}
