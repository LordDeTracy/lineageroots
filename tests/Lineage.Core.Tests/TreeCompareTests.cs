using System.Diagnostics;
using Lineage.Core.Analysis;
using Lineage.Core.Editing;
using Lineage.Core.Model;
using Lineage.Core.Storage;
using Xunit.Abstractions;

namespace Lineage.Core.Tests;

public class TreeCompareTests
{
    private readonly ITestOutputHelper _output;
    public TreeCompareTests(ITestOutputHelper output) => _output = output;

    private static Person P(FamilyTree t, string given, string surname, Sex sex, string? birth = null, string place = "")
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.PrimaryName.Given = given;
        p.PrimaryName.Surname = surname;
        if (birth != null) p.Events.Add(Event.Of("BIRT", GenDate.Parse(birth), place));
        return p;
    }

    private static Family Couple(FamilyTree t, Person? a, Person? b, params Person[] kids)
    {
        var f = t.AddFamily(new Family());
        if (a != null) t.SetPartner(f, a, second: false);
        if (b != null) t.SetPartner(f, b, second: true);
        foreach (var k in kids) t.AddChild(f, k);
        return f;
    }

    private static string Dump(FamilyTree t) => string.Join("\n",
        t.People.Values.OrderBy(p => p.Id).Select(LineageStore.ToJson)
         .Concat(t.Families.Values.OrderBy(f => f.Id).Select(LineageStore.ToJson))
         .Concat(t.Sources.Values.OrderBy(s => s.Id).Select(LineageStore.ToJson)));

    [Fact]
    public void MatchesByRecordFamilySearchIdAndLikeness()
    {
        var mine = new FamilyTree();
        var william = P(mine, "William", "Pemberwick", Sex.Male, "1820");
        var john = P(mine, "John", "Pemberwick", Sex.Male, "1850", "Leeds, Yorkshire");
        Couple(mine, william, null, john);
        var mary = P(mine, "Mary", "Jones", Sex.Female, "1855");
        mary.FamilySearchId = "LZ12-345";
        var henry = P(mine, "Henry", "Pemberwick", Sex.Male, "1880");
        var alice = P(mine, "Alice", "Brown", Sex.Female, "1900");

        var other = new FamilyTree();
        var henry2 = P(other, "Henry", "Pemberwick", Sex.Male, "1881");
        henry2.Uid = henry.Uid;                                              // same origin
        var mary2 = P(other, "Mary Ann", "Jones", Sex.Female, "1855");
        mary2.FamilySearchId = "lz12-345 ";
        var william2 = P(other, "William", "Pemberwyck", Sex.Male, "1820");
        var john2 = P(other, "John", "Pemberwyck", Sex.Male, "ABT 1851", "Leeds");
        Couple(other, william2, null, john2);
        var zed = P(other, "Zed", "Nobody", Sex.Male, "1700");
        var johnF = P(other, "John", "Pemberwick", Sex.Female, "1850");           // different sex

        var result = TreeCompare.Compare(mine, other);

        var h = result.ForOther(henry2.Id)!;
        Assert.Equal((henry, MatchKind.SameRecord, "Same record", 100), (h.Mine, h.Kind, h.How, h.Score));
        var bd = Assert.Single(h.Differences);
        Assert.Equal(("birth.date", "Birth date", "1880", "1881"), (bd.Key, bd.Label, bd.Mine, bd.Theirs));

        var m = result.ForOther(mary2.Id)!;
        Assert.Equal((mary, MatchKind.SameFamilySearchId, "Same FamilySearch ID"), (m.Mine, m.Kind, m.How));
        Assert.Equal(new[] { "name" }, m.Differences.Select(d => d.Key));

        var j = result.ForOther(john2.Id)!;
        Assert.Equal((john, MatchKind.Likely, "Likely match"), (j.Mine, j.Kind, j.How));
        Assert.InRange(j.Score, TreeCompare.DefaultMinScore, 100);
        Assert.Equal(new[] { "name", "birth.date", "birth.place", "father" }, j.Differences.Select(d => d.Key));
        Assert.Equal(("William Pemberwick", "William Pemberwyck"), (j.Differences[3].Mine, j.Differences[3].Theirs));
        Assert.Same(j, result.ForMine(john.Id));
        Assert.Same(william, result.ForOther(william2.Id)!.Mine);

        Assert.Equal(new[] { zed, johnF }, result.OnlyInOther);           // sorted by surname
        Assert.Equal(new[] { alice }, result.OnlyInMine);
        Assert.Equal(result.Matches.Count, result.Matches.Select(x => x.Mine.Id).Distinct().Count());
        Assert.Equal(result.Matches.Count, result.Matches.Select(x => x.Other.Id).Distinct().Count());

        var rejected = result.Without(j);
        Assert.Null(rejected.ForOther(john2.Id));
        Assert.Contains(john2, rejected.OnlyInOther);
        Assert.Contains(john, rejected.OnlyInMine);
    }

    [Fact]
    public void EachPersonMatchesAtMostOneBestFirst()
    {
        var mine = new FamilyTree();
        var t1850 = P(mine, "Thomas", "Green", Sex.Male, "1850");
        var t1853 = P(mine, "Thomas", "Green", Sex.Male, "1853");
        var other = new FamilyTree();
        var o1851 = P(other, "Thomas", "Green", Sex.Male, "1851");
        var o1853 = P(other, "Thomas", "Green", Sex.Male, "1853");
        var o1853b = P(other, "Thomas", "Green", Sex.Male, "1853");

        var result = TreeCompare.Compare(mine, other);
        Assert.Same(t1853, result.ForOther(o1853.Id)!.Mine);
        Assert.Same(t1850, result.ForOther(o1851.Id)!.Mine);
        Assert.Equal(new[] { o1853b }, result.OnlyInOther);
        Assert.Empty(result.OnlyInMine);
    }

    /// <summary>
    /// Mine: John + Mary with Tom; a parish register source.
    /// Other: the same couple and Tom, plus Ann (new) who married George (new) and had Baby (new).
    /// </summary>
    private static (FamilyTree Mine, FamilyTree Other, Family MineCouple, Source Register, Person Tom, Person[] New) ImportSample()
    {
        var mine = new FamilyTree();
        var register = mine.AddSource(new Source { Title = "Parish register of Testville" });
        var john = P(mine, "John", "Pemberwick", Sex.Male, "1850");
        var mary = P(mine, "Mary", "Jones", Sex.Female, "1852");
        var tom = P(mine, "Tom", "Pemberwick", Sex.Male, "1875");
        var couple = Couple(mine, john, mary, tom);

        var other = new FamilyTree();
        var otherRegister = other.AddSource(new Source { Title = " parish register of Testville" });
        var census = other.AddSource(new Source { Title = "1881 census of Testville", RepositoryId = other.AddRepository(new Repository { Name = "Test archive" }).Id });
        var john2 = P(other, "John", "Pemberwick", Sex.Male, "1850");
        var mary2 = P(other, "Mary", "Jones", Sex.Female, "1852");
        var tom2 = P(other, "Tom", "Pemberwick", Sex.Male, "1875");
        var ann = P(other, "Ann", "Pemberwick", Sex.Female, "1877");
        ann.Events[0].Citations.Add(new Citation { SourceId = otherRegister.Id, Page = "f. 12" });
        ann.Events[0].Citations.Add(new Citation { SourceId = census.Id, Page = "p. 3" });
        ann.Citations.Add(new Citation { SourceId = 999 });                  // a broken citation is dropped
        Couple(other, john2, mary2, tom2, ann);
        var george = P(other, "George", "Brown", Sex.Male, "1870");
        var baby = P(other, "Baby", "Brown", Sex.Female, "1900");
        var marriage = Couple(other, george, ann, baby);
        marriage.Events.Add(new Event { Tag = "MARR", Date = GenDate.Parse("1898"), Citations = { new Citation { SourceId = census.Id } } });
        return (mine, other, couple, register, tom, new[] { ann, george, baby });
    }

    [Fact]
    public void ImportLinksToMatchedRelativesAndCopiesSources()
    {
        var (mine, other, couple, register, tom, chosen) = ImportSample();
        var result = TreeCompare.Compare(mine, other);
        Assert.Equal(3, result.Matches.Count);
        Assert.Equal(chosen.OrderBy(p => p.PrimaryName.SortKey), result.OnlyInOther);
        var history = new UndoHistory(mine);
        var before = Dump(mine);
        var ids = chosen.Select(p => p.Id).ToList();

        var edit = history.Begin("Import", TreeCompare.ImportTouched(mine, other, ids, result));
        var added = TreeCompare.Import(mine, other, ids, result);
        edit.Commit();

        Assert.Equal(3, added.Count);
        var (ann, george, baby) = (mine.People[added[0]], mine.People[added[1]], mine.People[added[2]]);
        Assert.Equal(("Ann", "George", "Baby"), (ann.PrimaryName.Given, george.PrimaryName.Given, baby.PrimaryName.Given));
        Assert.Equal(chosen[0].Uid, ann.Uid);                                 // kept, so a later compare says "Same record"
        Assert.Equal(6, mine.People.Count);

        // Ann joins my existing family of John and Mary, next to Tom.
        Assert.Equal(new[] { tom.Id, ann.Id }, couple.Children.Select(c => c.ChildId));
        Assert.Equal(new[] { couple.Id }, ann.ParentFamilyIds);
        // Her own family is new, with her new husband and child.
        var marriage = mine.Family(Assert.Single(ann.SpouseFamilyIds))!;
        Assert.Equal((george.Id, ann.Id), (marriage.Partner1Id, marriage.Partner2Id));
        Assert.Equal(new[] { baby.Id }, marriage.Children.Select(c => c.ChildId));
        Assert.Equal(new[] { marriage.Id }, george.SpouseFamilyIds);
        Assert.Equal(new[] { marriage.Id }, baby.ParentFamilyIds);
        Assert.Equal(2, mine.Families.Count);

        // The register already existed (same title); the census was copied with its archive.
        Assert.Equal(2, mine.Sources.Count);
        var census = mine.Sources.Values.Single(s => s.Title.StartsWith("1881"));
        Assert.Equal(new[] { register.Id, census.Id }, ann.Birth!.Citations.Select(c => c.SourceId));
        Assert.Empty(ann.Citations);
        Assert.Equal(census.Id, marriage.Events.Single().Citations.Single().SourceId);
        Assert.Equal("Test archive", mine.Repositories[census.RepositoryId!.Value].Name);

        // The other tree is untouched, and undo takes it all back.
        Assert.Equal(6, other.People.Count);
        Assert.Equal(999, chosen[0].Citations.Single().SourceId);
        Assert.Equal(new[] { 1L }, chosen[0].ParentFamilyIds);
        history.Undo();
        Assert.Equal(before, Dump(mine));
        Assert.Single(mine.Families);
    }

    [Fact]
    public void ImportSkipsPeopleAlreadyMatched()
    {
        var (mine, other, _, _, _, _) = ImportSample();
        var result = TreeCompare.Compare(mine, other);
        var john2 = other.People.Values.First(p => p.PrimaryName.Given == "John");
        Assert.Empty(TreeCompare.Import(mine, other, new[] { john2.Id }, result));
        Assert.Equal(3, mine.People.Count);
    }

    [Fact]
    public void ImportedParentsBecomeTheParentsOfMyPerson()
    {
        var mine = new FamilyTree();
        var tom = P(mine, "Tom", "Pemberwick", Sex.Male, "1875");
        var other = new FamilyTree();
        var john = P(other, "John", "Pemberwick", Sex.Male, "1850");
        var mary = P(other, "Mary", "Jones", Sex.Female, "1852");
        var tom2 = P(other, "Tom", "Pemberwick", Sex.Male, "1875");
        Couple(other, john, mary, tom2);

        var result = TreeCompare.Compare(mine, other);
        Assert.Same(tom, result.ForOther(tom2.Id)!.Mine);
        var touched = TreeCompare.ImportTouched(mine, other, new[] { john.Id, mary.Id }, result);
        Assert.Contains(tom, touched);
        TreeCompare.Import(mine, other, new[] { john.Id, mary.Id }, result);

        var (father, mother) = mine.PrimaryParents(tom);
        Assert.Equal(("John", "Mary"), (father?.PrimaryName.Given, mother?.PrimaryName.Given));
    }

    [Fact]
    public void ComparesFiveThousandPeopleQuickly()
    {
        var mine = Synthetic(5000, seed: 7);
        var other = Variant(mine, seed: 11);
        var watch = Stopwatch.StartNew();
        var result = TreeCompare.Compare(mine, other);
        watch.Stop();
        var right = result.Matches.Count(m => m.Mine.Id == m.Other.Id);
        _output.WriteLine($"{result.Matches.Count} matches ({right} right) in {watch.ElapsedMilliseconds} ms");
        Assert.True(right >= 4500, $"only {right} right matches");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    /// <summary>An invented tree: surnames skewed towards a few common ones, couples with children.</summary>
    private static FamilyTree Synthetic(int count, int seed)
    {
        var rng = new Random(seed);
        string[] starts = { "Ash", "Bar", "Cal", "Dun", "Ell", "Fen", "Gar", "Hal", "Ing", "Kel", "Lam", "Mor", "Nor", "Pen", "Rad", "Sal", "Tal", "Wen", "Yor", "Cro" };
        string[] ends = { "ton", "ley", "by", "wood", "field", "more", "well", "ford", "ham", "son" };
        var surnames = starts.SelectMany(a => ends.Select(b => a + b)).ToArray();
        string[] men = { "John", "William", "Thomas", "James", "George", "Henry", "Robert", "Joseph", "Charles", "Edward",
                         "Samuel", "Richard", "Frederick", "Arthur", "Walter", "Albert", "Alfred", "Harry", "Benjamin", "David" };
        string[] women = { "Mary", "Elizabeth", "Sarah", "Ann", "Jane", "Margaret", "Emma", "Alice", "Ellen", "Hannah",
                           "Martha", "Eliza", "Emily", "Harriet", "Catherine", "Louisa", "Frances", "Clara", "Edith", "Florence" };
        var t = new FamilyTree();
        for (var i = 0; i < count; i++)
        {
            var sex = rng.Next(2) == 0 ? Sex.Male : Sex.Female;
            var names = sex == Sex.Male ? men : women;
            var given = names[rng.Next(names.Length)] + (rng.Next(3) == 0 ? " " + names[rng.Next(names.Length)] : "");
            var surname = surnames[(int)(Math.Pow(rng.NextDouble(), 2) * surnames.Length)];
            var p = P(t, given, surname, sex, (1700 + rng.Next(300)).ToString(), $"Town{rng.Next(100)}, Shire{rng.Next(10)}");
            if (rng.Next(3) == 0) p.Events.Add(Event.Of("DEAT", GenDate.Parse((p.Birth!.Date.Year + 20 + rng.Next(60)).ToString())));
        }
        var people = t.People.Values.ToList();
        var husbands = people.Where(p => p.Sex == Sex.Male).ToList();
        var wives = people.Where(p => p.Sex == Sex.Female).ToList();
        var next = 0;
        for (var i = 0; i < Math.Min(husbands.Count, wives.Count) / 2; i++)
        {
            var kids = new List<Person>();
            for (var k = 0; k < 3 && next < people.Count; k++, next++)
                if (people[next] != husbands[i] && people[next] != wives[i]) kids.Add(people[next]);
            Couple(t, husbands[i], wives[i], kids.ToArray());
        }
        return t;
    }

    /// <summary>Someone else's copy of the tree: new record ids (Uids), some dates a year out, some middle names missing.</summary>
    private static FamilyTree Variant(FamilyTree source, int seed)
    {
        var rng = new Random(seed);
        var t = new FamilyTree();
        foreach (var p in source.People.Values)
        {
            var c = LineageStore.FromJson<Person>(LineageStore.ToJson(p));
            c.Uid = Guid.NewGuid();
            if (rng.Next(4) == 0) c.Birth!.Date = GenDate.Parse((p.Birth!.Date.Year + 1).ToString());
            if (rng.Next(10) == 0) c.PrimaryName.Given = c.PrimaryName.Given.Split(' ')[0];
            t.AddPerson(c);
        }
        foreach (var f in source.Families.Values)
        {
            var c = LineageStore.FromJson<Family>(LineageStore.ToJson(f));
            c.Uid = Guid.NewGuid();
            t.AddFamily(c);
        }
        return t;
    }
}
