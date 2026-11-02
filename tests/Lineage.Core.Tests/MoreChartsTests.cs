using Lineage.Core.Charts;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class MoreChartsTests
{
    /// <summary>Walter and Edith; their children John and Rose; John's daughter Ann; Rose's son Paul.</summary>
    private static (FamilyTree Tree, Dictionary<string, Person> P) Family()
    {
        var t = new FamilyTree();
        var p = new Dictionary<string, Person>();
        Person Add(string given, Sex sex, string born, string died = "")
        {
            var x = t.AddPerson(new Person { Sex = sex });
            (x.PrimaryName.Given, x.PrimaryName.Surname) = (given, "Gray");
            if (born.Length > 0) x.Events.Add(Event.Of("BIRT", GenDate.Parse(born), "Leeds, Yorkshire"));
            if (died.Length > 0) x.Events.Add(Event.Of("DEAT", GenDate.Parse(died)));
            return p[given] = x;
        }
        void Couple(Person a, Person b, params Person[] kids)
        {
            var f = t.AddFamily(new Family());
            t.SetPartner(f, a, second: false);
            t.SetPartner(f, b, second: true);
            foreach (var k in kids) t.AddChild(f, k, new ChildLink { ChildId = k.Id });
        }
        Add("Walter", Sex.Male, "1800", "1870"); Add("Edith", Sex.Female, "1805", "1880");
        Add("John", Sex.Male, "1830", "1890"); Add("Rose", Sex.Female, "1833", "1899");
        Add("Tom", Sex.Male, "1830", "1880"); Add("Mary", Sex.Female, "1835", "1900");
        Add("Ann", Sex.Female, "1860"); Add("Paul", Sex.Male, "1862", "1940");
        Couple(p["Walter"], p["Edith"], p["John"], p["Rose"]);
        Couple(p["John"], p["Mary"], p["Ann"]);
        Couple(p["Tom"], p["Rose"], p["Paul"]);
        return (t, p);
    }

    [Fact]
    public void LifelinesShowTheFamilyAcrossTime()
    {
        var (t, p) = Family();
        var rows = MoreCharts.Lifelines(t, p["John"].Id, currentYear: 2026);
        Assert.Equal("Walter Gray", rows[0].Name);   // earliest first
        var john = rows.Single(r => r.IsRoot);
        Assert.Equal((1830, 1890, ""), (john.From, john.To, john.Relation));
        Assert.Equal("Father", rows.Single(r => r.Name == "Walter Gray").Relation);
        Assert.Equal("Sister", rows.Single(r => r.Name == "Rose Gray").Relation);
        var ann = rows.Single(r => r.Name == "Ann Gray");   // no death recorded: an estimated end
        Assert.Equal((1860, 1930, true), (ann.From, ann.To, ann.ToEstimated));
        Assert.DoesNotContain(rows, r => r.Name == "Paul Gray" && r.Relation == "");   // a nephew, related
    }

    [Fact]
    public void ARelationshipPathGoesUpToTheCommonAncestorAndDown()
    {
        var (t, p) = Family();
        var steps = MoreCharts.RelationshipPath(t, p["Ann"].Id, p["Paul"].Id);   // first cousins
        Assert.Equal(new[] { "Ann", "John", "Walter", "Rose", "Paul" },
                     steps.Select(s => t.Person(s.PersonId)!.PrimaryName.Given));
        Assert.Equal(new[] { 2, 1, 0, 1, 2 }, steps.Select(s => s.Y));   // the grandparents at the top
        Assert.Equal(new[] { "", "father", "father", "daughter", "son" }, steps.Select(s => s.Link));

        var married = MoreCharts.RelationshipPath(t, p["John"].Id, p["Mary"].Id);
        Assert.Equal(("wife", 0), (married[1].Link, married[1].Y));   // across for a marriage
    }

    [Fact]
    public void StatisticsSumUpTheTree()
    {
        var (t, _) = Family();
        var s = MoreCharts.Statistics(t);
        Assert.Equal(8, s.People);
        Assert.Equal(("1830s", 4), (s.BirthsByDecade.Single(b => b.Label == "1830s").Label, s.BirthsByDecade.Single(b => b.Label == "1830s").Count));
        Assert.Equal(("Gray", 8), (s.Surnames[0].Label, s.Surnames[0].Count));
        Assert.Equal("Leeds", s.Places.Single().Label);
        Assert.Contains(s.LifespanByCentury, b => b.Label == "19th century");
    }
}
