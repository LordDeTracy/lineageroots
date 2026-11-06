using Lineage.Core.Places;

namespace Lineage.Core.Tests;

public class HistoricalPlaceTests
{
    [Fact]
    public void OldNamesAreRecognised()
    {
        Assert.Equal("Kaliningrad, Russia", Assert.Single(HistoricalPlaces.In("Königsberg, East Prussia, Germany"), h => h.Town).Today);
        Assert.Contains(HistoricalPlaces.In("Konigsberg, Prussia"), h => h.Old == "Königsberg");          // accents don't matter
        Assert.Equal("Middlesex", Assert.Single(HistoricalPlaces.In("Harrow, Middlesex, England")).Old);
        Assert.Empty(HistoricalPlaces.In("Leeds, Yorkshire, England"));
        Assert.Empty(HistoricalPlaces.In("Canton, Stark, Ohio, USA"));                                    // not Guangzhou
        Assert.Single(HistoricalPlaces.In("Bytown, Upper Canada"), h => h.Town);                          // a Canadian town in Canada
    }

    [Fact]
    public void TheModernPlaceIsUsedForLookingItUp()
    {
        Assert.Equal("Gdańsk, Poland", HistoricalPlaces.Modern("Danzig, West Prussia, Germany"));
        Assert.Equal("Harrow, Greater London, England", HistoricalPlaces.Modern("Harrow, Middlesex, England"));
        Assert.Null(HistoricalPlaces.Modern("Leeds, Yorkshire, England"));
        Assert.Equal("Breslau has been called Wrocław, Poland since 1945.",
            HistoricalPlaces.Describe(HistoricalPlaces.In("Breslau, Silesia")[0]));
    }

    [Fact]
    public void TheGazetteerFindsTownsByTheirOldNames()
    {
        var gdansk = Gazetteer.Default.Locate("Danzig, West Prussia");
        Assert.NotNull(gdansk);
        Assert.InRange(gdansk!.Point.Latitude, 54.2, 54.5);
        Assert.InRange(gdansk.Point.Longitude, 18.4, 18.8);
        Assert.NotNull(Gazetteer.Default.Locate("Christiania, Norway"));
    }
}
