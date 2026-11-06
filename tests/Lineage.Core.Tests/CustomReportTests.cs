using Lineage.Core.Export;
using Lineage.Core.Reports;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class CustomReportTests
{
    private static List<string> People(ReportDocument doc) =>
        doc.Blocks.OfType<Heading>().Where(h => h.Anchor?.StartsWith("p-") == true).Select(h => h.Text.Split(" · ")[0]).ToList();

    [Fact]
    public void AncestorsInGenerationOrderWithChosenSections()
    {
        var t = Whitfields();
        var doc = Narrative.Custom(t, t.People[5], new CustomReportOptions { People = BranchKind.Ancestors, Generations = 3, Facts = false, Family = false });
        Assert.Equal("Ancestors of Clara Whitfield", doc.Title);
        var names = People(doc);
        Assert.Equal("Clara Whitfield", names[0]);
        Assert.Equal(new[] { "Eliza Hartley", "Thomas Edward Whitfield" }, names.Skip(1).Take(2).Order());
        Assert.Equal(new[] { "George Whitfield", "Mary Ann Cole" }, names.Skip(3).Order());
        Assert.Contains(doc.Blocks.OfType<Heading>(), h => h.Text == "Grandparents");
        Assert.DoesNotContain(doc.Blocks, b => b is Table);                     // no facts tables
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Text.StartsWith("Clara Whitfield was born"));
        Assert.NotEmpty(doc.Notes);
        // Names of people in the report link to their entries, without numbers.
        var links = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Where(r => r.Link != null).ToList();
        Assert.Contains(links, r => r.Text == "Thomas Edward Whitfield" && r.Link == "p-1");
        Assert.DoesNotContain(doc.Blocks.OfType<Paragraph>(), p => p.Text.Contains("(0)"));
    }

    [Fact]
    public void DescendantsByNameWithFactsFamilyAndIndex()
    {
        var t = Whitfields();
        var doc = Narrative.Custom(t, t.People[1], new CustomReportOptions
        {
            Title = "Thomas's family", People = BranchKind.Descendants, Generations = 2, Order = CustomReportOrder.ByName,
            Story = false, Sources = false, Index = true, PagePerPerson = true,
        });
        Assert.Equal("Thomas's family", doc.Title);
        var names = People(doc);
        Assert.Contains("Henry Whitfield", names);
        Assert.Contains("Ada Moss", names);                                     // a child's wife
        Assert.DoesNotContain("Alice Pryor", names);                            // a grandchild: beyond 2 generations
        Assert.Equal(names.OrderBy(n => n.Split(' ')[^1]).ThenBy(n => n), names);
        Assert.NotEmpty(doc.Blocks.OfType<Table>());
        Assert.Contains(doc.Blocks.OfType<BulletList>().SelectMany(l => l.Items), i => string.Concat(i.Runs.Select(r => r.Text)).StartsWith("Married: Eliza Hartley"));
        Assert.Empty(doc.Notes);
        Assert.NotEmpty(doc.Index);
        Assert.Equal(names.Count - 1, doc.Blocks.OfType<PageBreak>().Count());
    }

    [Fact]
    public void JustThePerson()
    {
        var t = Whitfields();
        var doc = Narrative.Custom(t, t.People[6], new CustomReportOptions { People = BranchKind.Ancestors, Generations = 1 });
        Assert.Equal(new[] { "Sam Whitfield" }, People(doc));
        Assert.Equal(t.People.Count, People(Narrative.Custom(t, t.People[6], new CustomReportOptions { People = BranchKind.Everyone, Order = CustomReportOrder.ByName })).Count);
    }
}
