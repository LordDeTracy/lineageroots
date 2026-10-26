using System.Diagnostics;
using Lineage.Core.Analysis;
using Lineage.Core.Model;
using Xunit.Abstractions;

namespace Lineage.Core.Tests;

public class AnalysisPerformanceTests(ITestOutputHelper output)
{
    private static readonly string[] Men = { "John", "William", "Thomas", "George", "James", "Henry", "Arthur", "Walter", "Albert", "Frank", "Harry", "Edward" };
    private static readonly string[] Women = { "Mary", "Ann", "Elizabeth", "Sarah", "Jane", "Emma", "Alice", "Ellen", "Martha", "Edith", "Annie", "Florence" };
    private static readonly string[] Starts = { "Ash", "Brad", "Cole", "Dun", "Fair", "Green", "Hart", "Kings", "Lang", "Mill", "North", "Pen", "Rad", "Sax", "Thorn", "Wood", "Pemberwick", "Black", "Whit", "Ox" };
    private static readonly string[] Ends = { "", "ford", "ley", "ton", "well", "by", "wood", "field", "er", "son" };

    /// <summary>
    /// An invented tree of about 15,000 people over several generations: couples with 1–5 children, full dates,
    /// marriages, and a sprinkling of deliberate mistakes so the problem finder has something to report.
    /// </summary>
    internal static FamilyTree Synthetic(int target = 15_000, int seed = 1234)
    {
        var rnd = new Random(seed);
        var t = new FamilyTree();
        string Surname() => Starts[rnd.Next(Starts.Length)] + Ends[rnd.Next(Ends.Length)];
        Person New(int year, string surname)
        {
            var male = rnd.Next(2) == 0;
            var p = t.AddPerson(new Person { Sex = male ? Sex.Male : Sex.Female });
            p.Names.Add(new Name { Given = male ? Men[rnd.Next(Men.Length)] : Women[rnd.Next(Women.Length)], Surname = surname });
            var birth = new DateOnly(year, rnd.Next(1, 13), rnd.Next(1, 29));
            p.Events.Add(Event.Of("BIRT", new GenDate { Year = birth.Year, Month = birth.Month, Day = birth.Day }, "Leeds"));
            var died = rnd.Next(100) < 3 ? year - 5 : year + rnd.Next(1, 90);   // 3% die before they are born
            p.Events.Add(Event.Of("DEAT", new GenDate { Year = died, Qualifier = rnd.Next(4) == 0 ? DateQualifier.About : DateQualifier.None }));
            if (rnd.Next(10) == 0) p.Events.Add(Event.Of("OCCU", GenDate.Parse("about the time of the war")));
            return p;
        }

        var generation = new List<Person>();
        for (var i = 0; i < 1200; i++) generation.Add(New(1650 + rnd.Next(20), Surname()));
        while (t.People.Count < target && generation.Count > 1)
        {
            var men = generation.Where(p => p.Sex == Sex.Male).OrderBy(_ => rnd.Next()).ToList();
            var women = generation.Where(p => p.Sex == Sex.Female).OrderBy(_ => rnd.Next()).ToList();
            var next = new List<Person>();
            for (var i = 0; i < Math.Min(men.Count, women.Count) && t.People.Count < target; i++)
            {
                var f = t.AddFamily(new Family());
                t.SetPartner(f, men[i], second: false);
                t.SetPartner(f, women[i], second: true);
                var start = Math.Max(men[i].Birth!.Date.Year, women[i].Birth!.Date.Year) + 20 + rnd.Next(10);
                f.Events.Add(Event.Of("MARR", new GenDate { Year = start, Month = rnd.Next(1, 13), Day = rnd.Next(1, 29) }));
                var kids = rnd.Next(1, 6);
                for (var k = 0; k < kids && t.People.Count < target; k++)
                {
                    var child = New(start + 1 + 2 * k + rnd.Next(2), men[i].Names[0].Surname);
                    t.AddChild(f, child);
                    next.Add(child);
                }
            }
            // Bring in some partners from outside the tree so every generation can marry.
            for (var i = 0; i < next.Count / 3 && t.People.Count < target; i++)
                next.Add(New(next[i].Birth!.Date.Year + rnd.Next(-3, 4), Surname()));
            generation = next;
        }
        return t;
    }

    [Fact]
    public void AnalysisIsFastOnALargeTree()
    {
        var t = Synthetic();
        Assert.InRange(t.People.Count, 14_000, 15_000);

        // Warm up once so the timings measure the work, not the JIT.
        Problems.Find(t);
        NameSearch.Find(t, "John Pemberwick", true);

        var sw = Stopwatch.StartNew();
        var problems = Problems.Find(t);
        var problemTime = sw.ElapsedMilliseconds;
        Assert.Contains(problems, p => p.Code == ProblemCodes.DeathBeforeBirth);
        Assert.Contains(problems, p => p.Code == ProblemCodes.UnreadableDate);
        output.WriteLine($"{t.People.Count} people: Problems.Find {problemTime} ms, {problems.Count} problems");
        foreach (var g in problems.GroupBy(p => (p.Code, p.Severity)).OrderByDescending(g => g.Count()))
            output.WriteLine($"  {g.Key.Code} ({g.Key.Severity}): {g.Count()}  e.g. {g.First().Message}");
        Assert.True(problemTime < 2000, $"Problems.Find took {problemTime} ms");

        sw.Restart();
        var pemberwicks = NameSearch.Find(t, "John Pemberwick", true);
        var byYear = NameSearch.Find(t, "Mary Ashford 1800", true);
        var searchTime = sw.ElapsedMilliseconds / 2;
        Assert.NotEmpty(pemberwicks);
        _ = byYear;
        output.WriteLine($"NameSearch.Find {searchTime} ms per query ({pemberwicks.Count} for John Pemberwick)");
        Assert.True(searchTime < 200, $"NameSearch.Find took {searchTime} ms");

        sw.Restart();
        var people = t.People.Values.ToList();
        var r = Relationship.Describe(t, people[^1], people[^500]);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Relationship.Describe took {sw.ElapsedMilliseconds} ms");
        Assert.NotNull(r.Text);

        sw.Restart();
        Dashboard.Stats(t);
        Dashboard.MissingData(t);
        Dashboard.Anniversaries(t, new DateOnly(2026, 12, 20), 30);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Dashboard took {sw.ElapsedMilliseconds} ms");
    }
}
