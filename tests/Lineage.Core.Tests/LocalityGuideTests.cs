using Lineage.Core.Places;

namespace Lineage.Core.Tests;

public class LocalityGuideTests
{
    [Fact]
    public void RecordsForThePlaceAndItsYears()
    {
        var leeds = LocalityGuides.For("Leeds, Yorkshire, England", 1842, 1911).Select(r => r.Name).ToList();
        Assert.Contains("Civil registration: births, marriages, deaths", leeds);
        Assert.Contains("Censuses", leeds);
        Assert.DoesNotContain("Land tax assessments", leeds);                  // ended 1832
        Assert.DoesNotContain(leeds, n => n.Contains("Old parish"));             // Scotland's

        Assert.Contains(LocalityGuides.For("Cardiff, Glamorgan, Wales", 1850), r => r.Name == "Censuses");   // one system with England
        Assert.Contains(LocalityGuides.For("Worcester, Massachusetts, USA", 1890, 1911), r => r.Name == "Federal censuses");
        Assert.Empty(LocalityGuides.For("Somewhere"));
        Assert.All(LocalityGuides.All, r => Assert.True(r.To == 0 || r.To >= r.From, r.Name));
    }
}
