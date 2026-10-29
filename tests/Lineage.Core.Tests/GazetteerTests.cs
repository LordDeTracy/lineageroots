using Lineage.Core.Model;
using Lineage.Core.Places;

namespace Lineage.Core.Tests;

public class GazetteerTests
{
    private static GeoMatch? Locate(string place) => Gazetteer.Default.Locate(place);

    private static void Near(GeoMatch? m, double lat, double lon)
    {
        Assert.NotNull(m);
        Assert.InRange(m!.Point.Latitude, lat - 0.3, lat + 0.3);
        Assert.InRange(m.Point.Longitude, lon - 0.3, lon + 0.3);
    }

    [Fact]
    public void TownsAreFoundInTheRightCountry()
    {
        var leeds = Locate("Leeds, Yorkshire, England");
        Near(leeds, 53.80, -1.55);
        Assert.True(leeds!.Sure);
        Assert.Equal(GeoPrecision.Town, leeds.Precision);
        Assert.Contains("England", leeds.Description);

        Near(Locate("Halifax, Yorkshire, England"), 53.72, -1.86);
        Near(Locate("Halifax, Nova Scotia, Canada"), 44.65, -63.58);
        Near(Locate("Skibbereen, Co. Cork, Ireland"), 51.55, -9.27);
        Near(Locate("Paris, France"), 48.85, 2.35);
    }

    [Fact]
    public void TheStateInTheNamePicksBetweenTownsOfTheSameName()
    {
        Near(Locate("Springfield, Sangamon, Illinois, USA"), 39.80, -89.64);
        Near(Locate("Springfield, Hampden, Massachusetts, USA"), 42.10, -72.59);
        Near(Locate("Worcester, MA"), 42.26, -71.80);
        // No town of that name in the state: only the state's centre, never a same-named town elsewhere.
        Assert.Equal(GeoPrecision.State, Locate("Leeds, NV")!.Precision);
    }

    [Fact]
    public void AStateAloneGivesItsCentreAndUnknownsAreLeftAlone()
    {
        var ohio = Locate("Ohio");
        Assert.NotNull(ohio);
        Assert.Equal(GeoPrecision.State, ohio!.Precision);
        Assert.False(ohio.Sure);
        Assert.InRange(ohio.Point.Latitude, 38.5, 41.9);

        Assert.Null(Locate("Leeds"));                         // no country: could be anywhere
        Assert.Null(Locate("Lillby, Nordmark, Swed"));        // an invented town
        Assert.Null(Locate(""));
    }

    [Fact]
    public void SuggestionsCoverPlacesWithoutGps()
    {
        var t = new FamilyTree();
        var p = t.AddPerson(new Person());
        p.Events.Add(Event.Of("BIRT", GenDate.Parse("1840"), "Leeds, Yorkshire, England"));
        p.Events.Add(Event.Of("DEAT", GenDate.Parse("1900"), "Otley, Yorkshire, England"));
        p.Events[1].Location = new GeoPoint(53.9, -1.69);   // already has GPS
        p.Events.Add(Event.Of("RESI", GenDate.Parse("1880"), "Nowhere in particular"));
        var s = Assert.Single(Gazetteer.Default.Suggest(t));
        Assert.Equal("Leeds, Yorkshire, England", s.Place.Name);
    }

    [Fact]
    public void MapLinksGoStraightToTheTownWhenItIsKnown()
    {
        // OpenStreetMap's own search is rate-limited (error 429) and can pick a same-named town elsewhere.
        var url = Gazetteer.MapUrl("Burlington, Chittenden, Vermont, USA");
        Assert.DoesNotContain("search", url);
        Assert.Contains("44.4", url);   // Burlington, Vermont, not Burlington in England or Ontario
        Assert.Contains("search?query=", Gazetteer.MapUrl("Qzxv Farm, Nowhereshire"));
    }
}
