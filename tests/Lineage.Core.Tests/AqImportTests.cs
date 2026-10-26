using Lineage.Core.Import.Aq;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using Xunit.Abstractions;

namespace Lineage.Core.Tests;

/// <summary>
/// Cross-checks the .aq importer against AQ's own GEDCOM export of the same database: both
/// importers must agree on people, names, sex, dates, places, families and children.
/// </summary>
public class AqImportTests(ITestOutputHelper output)
{
    public static TheoryData<string> Names => Samples.Names();

    [Theory]
    [MemberData(nameof(Names))]
    public void AqImportMatchesAqsOwnExport(string name)
    {
        if (Samples.Get(name) is not var (aqPath, gedPath)) { output.WriteLine($"sample {name} not available"); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var aq = AqImporter.Import(aqPath);
        output.WriteLine($"{name}: AQ import {sw.ElapsedMilliseconds} ms, {aq.People.Count} people, {aq.Families.Count} families");
        var ged = GedcomImporter.ImportFile(gedPath);

        Assert.Equal(ged.People.Count, aq.People.Count);
        Assert.Equal(ged.Families.Count, aq.Families.Count);
        Assert.Equal(ged.Sources.Count, aq.Sources.Count);

        var problems = new List<string>();
        foreach (var gp in ged.People.Values)
        {
            var ap = aq.People[gp.Id];
            // A '/' inside a stored name can't survive GEDCOM's NAME syntax; skip those names.
            if (!ap.PrimaryName.Given.Contains('/') && !ap.PrimaryName.Surname.Contains('/'))
            {
                Check(problems, gp.Id, "given", gp.PrimaryName.Given, ap.PrimaryName.Given);
                Check(problems, gp.Id, "surname", gp.PrimaryName.Surname, ap.PrimaryName.Surname);
            }
            Check(problems, gp.Id, "sex", gp.Sex, ap.Sex);
            foreach (var tag in new[] { "BIRT", "CHR", "DEAT", "BURI" })
            {
                Check(problems, gp.Id, tag + " date", gp.Find(tag)?.Date.ToGedcom() ?? "", ap.Find(tag)?.Date.ToGedcom() ?? "");
                Check(problems, gp.Id, tag + " place", gp.Find(tag)?.Place ?? "", ap.Find(tag)?.Place ?? "");
            }
            Check(problems, gp.Id, "parents", string.Join(",", gp.ParentFamilyIds), string.Join(",", ap.ParentFamilyIds));
            Check(problems, gp.Id, "spouse families", string.Join(",", gp.SpouseFamilyIds.Order()), string.Join(",", ap.SpouseFamilyIds.Order()));
            Check(problems, gp.Id, "events", gp.Events.Count, ap.Events.Count);
            Check(problems, gp.Id, "note", gp.Note, ap.Note);
        }
        foreach (var gf in ged.Families.Values)
        {
            var af = aq.Families[gf.Id];
            Check(problems, gf.Id, "partners", Set(gf.PartnerIds), Set(af.PartnerIds));
            Check(problems, gf.Id, "children", string.Join(",", gf.Children.Select(c => c.ChildId)), string.Join(",", af.Children.Select(c => c.ChildId)));
            Check(problems, gf.Id, "marriage date", gf.Marriage?.Date.ToGedcom() ?? "", af.Marriage?.Date.ToGedcom() ?? "");
            Check(problems, gf.Id, "divorced", Divorced(gf), Divorced(af));
            Check(problems, gf.Id, "unmarried", gf.Status == CoupleStatus.Unmarried, af.Status == CoupleStatus.Unmarried);
        }
        foreach (var p in problems.Take(20)) output.WriteLine(p);
        Assert.True(problems.Count == 0, $"{problems.Count} differences, first: {problems.FirstOrDefault()}");
    }

    private static string Set(IEnumerable<long> ids) => string.Join(",", ids.Order());
    private static bool Divorced(Family f) => f.Status == CoupleStatus.Divorced || f.Events.Any(e => e.Tag == "DIV");

    private static void Check<T>(List<string> problems, long id, string what, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            problems.Add($"#{id} {what}: AQ export={expected} | .aq import={actual}");
    }

    [Fact]
    public void Probe3SpecialFieldsDecode()
    {
        if (Samples.Get("probe3") is not var (aqPath, _)) return;
        var t = AqImporter.Import(aqPath);
        var ivan = t.People[1];
        Assert.Equal("Y-DNA", ivan.DnaTests.Single().Kind);
        Assert.Equal("R1bprobe", ivan.DnaTests[0].Haplogroup);
        Assert.Equal("KWZ1-ABC", ivan.FamilySearchId);
        Assert.Equal("ivan@probe.example", ivan.Email);
        Assert.Contains("Citytownprobe", ivan.Address);
        Assert.Equal("BIC", ivan.Find("BAPL")!.Status);
        Assert.Equal("Occupationtwoprobe", ivan.Find("OCCU")!.Value);
        Assert.Equal("Toplaceprobe", ivan.Find("OCCU")!.Place2);
    }

    [Fact]
    public void Probe5ColoursAndTags()
    {
        if (Samples.Get("probe5") is not var (aqPath, _)) return;
        var t = AqImporter.Import(aqPath);
        Assert.Equal("Red", t.People[1].Colour);
        Assert.Equal(new[] { "Tagonefive", "Tagtwofive", "Tagthreefive" }, t.People[2].Tags);
        Assert.Equal("Beige", t.People[3].Colour);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void DeleteChainHoldsOnlyMarkedRecords(string name)
    {
        // Header +0xE0 heads the free list of deleted citations (link at +0x20): every record on it
        // must carry the deleted marker, or the importer would skip live data.
        if (Samples.Get(name) is not var (aqPath, _)) return;
        var file = AqFile.Open(aqPath);
        var seen = new HashSet<long>();
        for (long n = file.HeaderU32(0xE0); n != 0 && seen.Add(n);)
        {
            var raw = file.Record(RecordType.Citation, n)!;
            Assert.True(RecordType.Citation.IsDeleted(raw), $"citation {n} on the delete chain is not marked deleted");
            n = BitConverter.ToUInt32(raw, 0x20);
        }
        output.WriteLine($"{name}: {seen.Count} deleted citations on the free list");
    }
}
