using Lineage.Core.Model;
using Lineage.Core.Reports;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class ReportFormatTests
{
    private static string Entry(ReportDocument doc, string anchor) =>
        Assert.Single(doc.Blocks.OfType<Paragraph>(), p => p.Anchor == anchor).Text;

    private static List<ListItem> Items(ReportDocument doc) => doc.Blocks.OfType<BulletList>().SelectMany(l => l.Items).ToList();

    [Fact]
    public void AhnentafelNumbersFathersTwiceAndMothersTwicePlusOne()
    {
        var t = Whitfields();
        var doc = Narrative.Ahnentafel(t, t.People[5], 3);          // Clara
        Assert.StartsWith("1. Clara Whitfield was born", Entry(doc, "a-1"));
        Assert.StartsWith("2. Thomas Edward Whitfield", Entry(doc, "a-2"));
        Assert.StartsWith("3. Eliza Hartley", Entry(doc, "a-3"));
        Assert.StartsWith("4. George Whitfield", Entry(doc, "a-4"));
        Assert.StartsWith("5. Mary Ann Cole", Entry(doc, "a-5"));
        Assert.Contains(doc.Blocks.OfType<Heading>(), h => h.Text == "Generation 3 — grandparents");
        Assert.NotEmpty(doc.Notes);
    }

    [Fact]
    public void NgsqNumbersEveryChildAndRegisterOnlyThoseCarriedOn()
    {
        var t = Whitfields();
        var thomas = t.People[1];
        // Thomas's children: Clara (a family), Sam (none), Henry (a family).
        var register = Items(Narrative.Register(t, thomas, 3)).Where(i => i.Runs.Any(r => r.Text.StartsWith("Clara") || r.Text.StartsWith("Sam") || r.Text.StartsWith("Henry"))).ToList();
        Assert.Equal(new[] { "+ 2  i.", "ii.", "+ 3  iii." }, register.Select(i => i.Label));

        var ngsq = Narrative.Ngsq(t, thomas, 3);
        var children = Items(ngsq).Where(i => i.Runs.Any(r => r.Text.StartsWith("Clara") || r.Text.StartsWith("Sam") || r.Text.StartsWith("Henry"))).ToList();
        Assert.Equal(new[] { "+ 2  i.", "3  ii.", "+ 4  iii." }, children.Select(i => i.Label));
        Assert.StartsWith("4. Henry Whitfield", Entry(ngsq, "d-4"));       // entries keep the child's own number
        Assert.DoesNotContain(ngsq.Blocks.OfType<Paragraph>(), p => p.Anchor == "d-3");   // Sam has no family to describe
    }

    [Fact]
    public void WhatsMissingListsTheGapsNearestFirst()
    {
        var t = Whitfields();
        var doc = Narrative.WhosMissing(t, t.People[5], 3, currentYear: 2026);
        var table = Assert.Single(doc.Blocks.OfType<Table>());
        var rows = table.Rows.Select(r => r.Select(c => string.Concat(c.Runs.Select(x => x.Text))).ToArray()).ToList();
        Assert.Equal(new[] { "1", "3", "4", "5" }, rows.Select(r => r[0]));   // Thomas (2) has everything recorded
        var mary = rows.Single(r => r[1] == "Mary Ann Cole");
        Assert.Contains("birth date", mary[3]);
        Assert.Contains("a source", mary[3]);
        Assert.DoesNotContain("father", mary[3]);                            // the last generation shown isn't a dead end
        var clara = rows.Single(r => r[1] == "Clara Whitfield");
        Assert.Contains("death", clara[3]);                                  // born 1872: long gone, but no death recorded
        Assert.DoesNotContain("birth place", clara[3]);
    }

    [Fact]
    public void BibliographyGroupsSourcesAndCountsTheirUse()
    {
        var t = Whitfields();
        t.AddSource(new Source { Template = "census-uk", Fields = { ["Year"] = "1881", ["Parish"] = "Leeds", ["County"] = "Yorkshire" } });
        var doc = Narrative.Bibliography(t);
        Assert.Contains(doc.Blocks.OfType<Heading>(), h => h.Text == "Census");
        var items = Items(doc).Select(i => string.Concat(i.Runs.Select(r => r.Text))).ToList();
        Assert.Contains(items, i => i.StartsWith("Church of England. Parish registers of St Mary, Harrow.") && i.EndsWith("(2)"));
        Assert.Contains(items, i => i.StartsWith("England. Yorkshire. Leeds. 1881 census.") && i.EndsWith("(not cited yet)"));
    }
}
