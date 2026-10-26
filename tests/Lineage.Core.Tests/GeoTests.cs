using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class GeoTests
{
    [Theory]
    [InlineData("42.2626, -71.8023")]
    [InlineData("42.2626,-71.8023")]
    [InlineData("42.2626 -71.8023")]
    [InlineData("N42.2626 W71.8023")]
    [InlineData("42.2626 N, 71.8023 W")]
    [InlineData("42.2626° N, 71.8023° W")]
    [InlineData("42°15'45.36\"N 71°48'08.28\"W")]
    [InlineData("W71.8023 N42.2626")]
    public void ParsesCommonForms(string text)
    {
        var p = GeoPoint.Parse(text);
        Assert.NotNull(p);
        Assert.Equal(42.2626, p!.Value.Latitude, 3);
        Assert.Equal(-71.8023, p.Value.Longitude, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Worcester")]
    [InlineData("95, 10")]        // latitude out of range
    [InlineData("10, 200")]       // longitude out of range
    [InlineData("E10 E20")]       // two longitudes
    public void RejectsNonsense(string text) => Assert.Null(GeoPoint.Parse(text));

    [Fact]
    public void GedcomForm()
    {
        var p = new GeoPoint(-33.8688, 151.2093);
        Assert.Equal("S33.8688", p.GedcomLatitude);
        Assert.Equal("E151.2093", p.GedcomLongitude);
        Assert.Equal(p, GeoPoint.FromGedcom(p.GedcomLatitude, p.GedcomLongitude));
        Assert.Equal("-33.86880, 151.20930", p.ToString());
    }
}
