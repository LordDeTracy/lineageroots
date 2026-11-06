using Lineage.Core.Export;
using Lineage.Core.Model;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class BranchTests
{
    private static HashSet<string> Names(FamilyTree t, IEnumerable<long> ids) => ids.Select(id => t.People[id].DisplayName).ToHashSet();

    [Fact]
    public void AncestorsDescendantsAndBoth()
    {
        var t = Whitfields();
        var thomas = t.People[1];
        Assert.Equal(new HashSet<string> { "Thomas Edward Whitfield", "George Whitfield", "Mary Ann Cole" },
            Names(t, Branch.People(t, thomas, BranchKind.Ancestors)));
        var down = Names(t, Branch.People(t, thomas, BranchKind.Descendants));
        Assert.Contains("Eliza Hartley", down);       // his wife
        Assert.Contains("Alice Pryor", down);         // a granddaughter
        Assert.Contains("John Pryor", down);          // her father, Clara's husband
        Assert.DoesNotContain("George Whitfield", down);
        Assert.Equal(t.People.Count, Branch.People(t, thomas, BranchKind.AncestorsAndDescendants).Count);
        Assert.DoesNotContain("John Pryor", Names(t, Branch.People(t, thomas, BranchKind.Descendants, spouses: false)));
    }

    [Fact]
    public void TheCopyKeepsOnlyWhatTheBranchNeeds()
    {
        var t = Whitfields();
        var clara = t.People[5];
        var unused = t.AddSource(new Source { Title = "Nothing to do with Clara" });
        var copy = Branch.Extract(t, Branch.People(t, clara, BranchKind.Descendants), "Pryor branch");

        Assert.Equal("Pryor branch", copy.Name);
        Assert.Equal(new HashSet<string> { "Clara Whitfield", "John Pryor", "Alice Pryor" }, copy.People.Values.Select(p => p.DisplayName).ToHashSet());
        Assert.Single(copy.Families);                                          // John and Clara with Alice
        var c = copy.People[clara.Id];
        Assert.Empty(c.ParentFamilyIds);                                       // her parents' family didn't come along
        Assert.DoesNotContain(copy.Sources.Values, s => s.Id == unused.Id);
        Assert.Equal(3, t.People.Values.Count(p => copy.People.ContainsKey(p.Id)));
        Assert.Equal(10, t.People.Count);                                      // the original is untouched

        // Ancestors keep the sources they cite.
        var up = Branch.Extract(t, Branch.People(t, t.People[1], BranchKind.Ancestors));
        Assert.Contains(up.Sources.Values, s => s.Title == "Parish registers of St Mary, Harrow");
        Assert.DoesNotContain(up.Sources.Values, s => s.Title == "Leeds marriages");   // only Thomas and Eliza's marriage cites it, and Eliza isn't an ancestor
        Assert.Empty(up.Families.Values.Where(f => f.Events.Any(e => e.Tag == "MARR" && e.Place.StartsWith("Leeds"))));
    }
}
