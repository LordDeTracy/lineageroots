using Lineage.Core.Analysis;
using Lineage.Core.Charts;
using Lineage.Core.Model;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class FeedbackFixTests
{
    [Fact]
    public void ARepeatedEventIsOneProblem()
    {
        var t = Whitfields();
        // Entered twice, as exported files often have it.
        t.People[1].Events.Add(E("OCCU", "1800", value: "Clerk"));
        t.People[1].Events.Add(E("OCCU", "1800", value: "Clerk"));
        Assert.Single(Problems.Find(t), p => p.Code == ProblemCodes.EventBeforeBirth && p.PersonId == 1);
    }

    [Fact]
    public void LifelinesShowOneSideOrTheDescendants()
    {
        var t = Whitfields();
        var clara = t.People[5];
        var names = (LifelineScope s) => MoreCharts.Lifelines(t, clara.Id, 2026, s).Select(r => r.Name).ToList();

        var father = names(LifelineScope.FatherSide);
        Assert.Contains("George Whitfield", father);
        Assert.DoesNotContain("Eliza Hartley", father);
        var mother = names(LifelineScope.MotherSide);
        Assert.Contains("Eliza Hartley", mother);
        Assert.DoesNotContain("George Whitfield", mother);

        var thomasLine = MoreCharts.Lifelines(t, 1, 2026, LifelineScope.Descendants).Select(r => r.Name).ToList();
        Assert.Contains("Alice Pryor", thomasLine);                // a grandchild
        Assert.DoesNotContain("George Whitfield", thomasLine);     // not an ancestor
        Assert.DoesNotContain("Eliza Hartley", thomasLine);        // nor a spouse
    }

    [Fact]
    public void AverageAgeCountsOnlyRecordedLives()
    {
        var rows = new[]
        {
            new LifeRow(1, "A", "", 1800, 1860, false, false, false),
            new LifeRow(2, "B", "", 1800, 1880, false, false, false),
            new LifeRow(3, "C", "", 1800, 1870, false, true, false),   // no recorded death
        };
        Assert.Equal((70.0, 2), MoreCharts.Longevity(rows));
        Assert.Equal((0.0, 0), MoreCharts.Longevity(Array.Empty<LifeRow>()));
    }
}
