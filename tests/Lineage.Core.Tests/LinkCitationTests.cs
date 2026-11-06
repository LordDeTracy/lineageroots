using Lineage.Core.Model;
using Lineage.Core.Sources;

namespace Lineage.Core.Tests;

public class LinkCitationTests
{
    private static readonly DateTime Today = new(2026, 3, 14);

    [Fact]
    public void KnownSitesAreRecognised()
    {
        var fs = LinkCitations.From("https://www.familysearch.org/ark:/61903/1:1:NKTG-5ZQ", Today)!;
        Assert.Equal(("website", "FamilySearch"), (fs.TemplateId, fs.Site));
        Assert.Equal("FamilySearch record NKTG-5ZQ", fs.Fields["Title"]);
        Assert.Equal("14 March 2026", fs.Fields["Accessed"]);

        var grave = LinkCitations.From("findagrave.com/memorial/12345/thomas-edward-whitfield", Today)!;
        Assert.Equal("Memorial 12345, Thomas Edward Whitfield", grave.Fields["Title"]);
        Assert.Equal("https://findagrave.com/memorial/12345/thomas-edward-whitfield", grave.Fields["Url"]);

        var paper = LinkCitations.From("https://chroniclingamerica.loc.gov/lccn/sn83045462/1911-11-10/ed-1/seq-3/", Today)!;
        Assert.Equal("newspaper", paper.TemplateId);
        Assert.Equal("10 November 1911", paper.Fields["Date"]);
    }

    [Fact]
    public void AnyOtherLinkIsAWebPage()
    {
        var page = LinkCitations.From("https://www.example-parish.org.uk/history/the-whitfield-family", Today)!;
        Assert.Equal(("website", "example-parish.org.uk"), (page.TemplateId, page.Fields["Website"]));
        Assert.Equal("The Whitfield Family", page.Fields["Title"]);
        Assert.Null(LinkCitations.From("not a link", Today));
    }

    [Fact]
    public void TheCitationReadsWell()
    {
        var found = LinkCitations.From("https://www.findagrave.com/memorial/12345/thomas-edward-whitfield", Today)!;
        var s = new Source { Template = found.TemplateId };
        foreach (var (k, v) in found.Fields) s.Fields[k] = v;
        Assert.Equal("Find a Grave, “Memorial 12345, Thomas Edward Whitfield,” database, Find a Grave (https://www.findagrave.com/memorial/12345/thomas-edward-whitfield : accessed 14 March 2026)",
            SourceTemplates.FullNote(s, ""));
    }
}
