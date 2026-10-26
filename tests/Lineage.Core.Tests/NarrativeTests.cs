using Lineage.Core.Model;
using Lineage.Core.Reports;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class NarrativeTests
{
    private static string Entry(ReportDocument doc, string anchor) =>
        Assert.Single(doc.Blocks.OfType<Paragraph>(), p => p.Anchor == anchor).Text;

    private static IEnumerable<Run> AllRuns(ReportDocument doc) => doc.Blocks.SelectMany(b => b switch
    {
        Paragraph p => p.Runs,
        BulletList l => l.Items.SelectMany(i => i.Runs),
        Table t => t.Rows.SelectMany(r => r.SelectMany(c => c.Runs)),
        _ => Enumerable.Empty<Run>(),
    });

    private static HashSet<string> Anchors(ReportDocument doc) =>
        doc.Blocks.Select(b => b switch { Heading h => h.Anchor, Paragraph p => p.Anchor, _ => null }).OfType<string>()
           .Concat(doc.Notes.Select(n => n.Anchor)).ToHashSet();

    [Fact]
    public void RegisterTellsALifeInPlainEnglish()
    {
        var t = Whitfields();
        var doc = Narrative.Register(t, t.People[1], 4);
        Assert.Equal("Descendants of Thomas Edward Whitfield", doc.Title);
        Assert.Equal(
            "1. Thomas Edward Whitfield was born on 14 March 1842 in Harrow, Middlesex, England[1], the son of George Whitfield and Mary Ann Cole. " +
            "He married Eliza Hartley on 18 June 1868 in Leeds, Yorkshire, England.[2] " +
            "Eliza was born in 1845 in Leeds, Yorkshire, England and died on 3 April 1920 in Worcester, Massachusetts, USA, aged about 75. " +
            "He worked as a stonemason (1871). " +
            "He died on 9 November 1911 in Worcester, Massachusetts, USA[1], aged 69, and was buried on 13 November 1911 in Hope Cemetery, Worcester, Massachusetts, USA.",
            Entry(doc, "d-1"));
        // the person's own note follows as paragraphs
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Text == "Thomas carved the font at St Mary’s.");
    }

    [Fact]
    public void PronounsFollowSex()
    {
        var t = Whitfields();
        var doc = Narrative.Register(t, t.People[1], 4);
        Assert.StartsWith("2. Clara Whitfield was born in 1872 in Leeds, Yorkshire, England, the daughter of Thomas Edward Whitfield (1) and Eliza Hartley. She married John Pryor.", Entry(doc, "d-2"));
        Assert.Contains("He married Ada Moss.", Entry(doc, "d-3"));

        // nobody knows whether Sam was a boy or a girl: "they", with "were"
        var sam = t.People[6];
        sam.Events.Add(E("OCCU", "1890", value: "Engineer"));
        sam.Events.Add(E("BURI", "", "Leeds"));
        var summary = Narrative.LifeSummary(t, sam);
        Assert.Equal("Sam Whitfield was born on 3 May 1874 in Leeds, Yorkshire, England, the child of Thomas Edward Whitfield and Eliza Hartley. " +
                     "They worked as an engineer (1890). They died in 1875, aged about 1, and were buried in Leeds.", summary);
    }

    [Fact]
    public void ChildrenAreListedWithRomanNumeralsAndPlusForTheirOwnEntries()
    {
        var t = Whitfields();
        var doc = Narrative.Register(t, t.People[1], 4);
        var children = doc.Blocks.OfType<BulletList>().First();
        Assert.Equal(new[] { "+ 2  i.", "ii.", "+ 3  iii." }, children.Items.Select(i => i.Label));
        Assert.Equal("d-2", children.Items[0].LabelAnchor);
        Assert.Equal("Clara Whitfield, born 1872 in Leeds, Yorkshire, England", Run.Join(children.Items[0].Runs));
        Assert.Equal("Sam Whitfield, born 3 May 1874 in Leeds, Yorkshire, England; died 1875", Run.Join(children.Items[1].Runs));
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Text == "Children of Thomas Edward Whitfield and Eliza Hartley:");
        Assert.Equal(new[] { "Generation 1", "Generation 2 — children" }, doc.Blocks.OfType<Heading>().Select(h => h.Text));

        // every link points at something that exists
        var anchors = Anchors(doc);
        Assert.All(AllRuns(doc).Where(r => r.Link != null), r => Assert.Contains(r.Link!, anchors));

        Assert.Equal("xiv", ReportText.Roman(14));
        Assert.Equal("xlix", ReportText.Roman(49));
    }

    [Fact]
    public void OneGenerationListsChildrenWithoutCarryingThemForward()
    {
        var t = Whitfields();
        var doc = Narrative.Register(t, t.People[1], 1);
        Assert.Equal(new[] { "i.", "ii.", "iii." }, doc.Blocks.OfType<BulletList>().Single().Items.Select(i => i.Label));
        Assert.Single(doc.Blocks.OfType<Heading>());
        Assert.Equal("Clara Whitfield, born 1872 in Leeds, Yorkshire, England; married John Pryor",
                     Run.Join(doc.Blocks.OfType<BulletList>().Single().Items[0].Runs));
    }

    [Fact]
    public void CitationsBecomeNumberedEndnotesAndRepeatsShareANumber()
    {
        var t = Whitfields();
        var doc = Narrative.Register(t, t.People[1], 4);
        Assert.Equal(new[] { 1, 2 }, doc.Notes.Select(n => n.Number));
        Assert.Equal("Church of England, Parish registers of St Mary, Harrow, Baptisms 1842, entry 118.", doc.Notes[0].Text);
        Assert.Equal("Leeds marriages (Leeds, 1868), Folio 22.", doc.Notes[1].Text);
        var refs = AllRuns(doc).Where(r => r.NoteNumber != null).Select(r => r.NoteNumber!.Value).ToList();
        Assert.Equal(new[] { 1, 2, 1 }, refs);                     // birth, marriage, death (same register as the birth)
    }

    [Fact]
    public void PrivateEventsAreLeftOutEverywhere()
    {
        var t = Whitfields();
        var secret = E("OCCU", "1885", "Secretville", "Spy", isPrivate: true);
        t.People[1].Events.Add(secret);
        t.Families[2].Events.Add(E("DIV", "1900", "Secretville", isPrivate: true));
        foreach (var doc in new[]
                 {
                     Narrative.Register(t, t.People[1], 4), Narrative.IndividualSummary(t, t.People[1]), Narrative.PlaceList(t),
                     Narrative.SurnameList(t), Narrative.BirthdayCalendar(t), Narrative.FamilyBook(t, t.People[5]),
                 })
        {
            var text = doc.PlainText();
            Assert.DoesNotContain("Secret", text);
            Assert.DoesNotContain("Spy", text);
            Assert.DoesNotContain("divorce", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("Secret", Narrative.LifeSummary(t, t.People[1]));
    }

    [Fact]
    public void DatesReadNaturally()
    {
        Assert.Equal("on 14 March 1842", ReportText.DatePhrase(GenDate.Parse("14 MAR 1842")));
        Assert.Equal("in March 1842", ReportText.DatePhrase(GenDate.Parse("MAR 1842")));
        Assert.Equal("in 1842", ReportText.DatePhrase(GenDate.Parse("1842")));
        Assert.Equal("about 1850", ReportText.DatePhrase(GenDate.Parse("ABT 1850")));
        Assert.Equal("before 1850", ReportText.DatePhrase(GenDate.Parse("BEF 1850")));
        Assert.Equal("between 1932 and 1933", ReportText.DatePhrase(GenDate.Parse("BET 1932 AND 1933")));
        Assert.Equal("from 1900 to 1910", ReportText.Date(GenDate.Parse("FROM 1900 TO 1910")));
        Assert.Equal("1691/92", ReportText.Date(GenDate.Parse("1691/1692")));
        Assert.Equal("aged 69", ReportText.Age(E("BIRT", "14 MAR 1842"), GenDate.Parse("9 NOV 1911")));
        Assert.Equal("aged 68", ReportText.Age(E("BIRT", "14 DEC 1842"), GenDate.Parse("9 NOV 1911")));
        Assert.Equal("aged about 69", ReportText.Age(E("BIRT", "ABT 1842"), GenDate.Parse("1911")));
        Assert.Equal("aged 3 months", ReportText.Age(E("BIRT", "1 JAN 1900"), GenDate.Parse("15 APR 1900")));
        Assert.Equal("", ReportText.Age(E("BIRT", "1900"), GenDate.Parse("BEF 1950")));
    }

    [Fact]
    public void MoreThanOneMarriageIsCounted()
    {
        var t = Whitfields();
        var henry = t.People[7];
        var second = P(t, "Ruth", "Lane", Sex.Female);
        var f = Couple(t, henry, second);
        f.Events.Add(E("MARR", "1910", "Boston, Massachusetts, USA"));
        var story = Narrative.LifeSummary(t, henry);
        Assert.Contains("He married, first, Ada Moss. He married, second, Ruth Lane in 1910 in Boston, Massachusetts, USA.", story);
    }

    [Fact]
    public void IndividualSummaryHasNamesFactsParentsFamiliesNotesAndSources()
    {
        var t = Whitfields();
        var doc = Narrative.IndividualSummary(t, t.People[1]);
        Assert.Equal("Thomas Edward Whitfield", doc.Title);
        Assert.Equal("Sources", doc.NotesHeading);
        Assert.Equal(new[] { "Names", "Facts and events", "Parents", "Family with Eliza Hartley", "Children (3)", "Notes" },
                     doc.Blocks.OfType<Heading>().Select(h => h.Text));
        var text = doc.PlainText();
        Assert.Contains("Tom Whitfield (also known as)", text);
        Assert.Contains("Father: George Whitfield (1815–)", text);
        Assert.Contains("Mother: Mary Ann Cole", text);
        Assert.Contains("Wife: Eliza Hartley (1845–1920, born in Leeds, Yorkshire, England)", text);
        Assert.Contains("He sailed for Boston in 1880.", text);

        var facts = doc.Blocks.OfType<Table>().First();
        Assert.Equal(new[] { "Event", "Date", "Place", "Details" }, facts.Headers);
        Assert.Equal(new[] { "Birth", "Occupation", "Death", "Burial" }, facts.Rows.Select(r => r[0].Text));
        Assert.Equal("14 March 1842", facts.Rows[0][1].Text);
        Assert.Equal("Stonemason", facts.Rows[1][3].Text);
        Assert.Equal(1, facts.Rows[0][3].Runs.Single(r => r.NoteNumber != null).NoteNumber);
        Assert.Equal(2, doc.Notes.Count);
    }

    [Fact]
    public void PlaceListCountsEventsPeopleAndYears()
    {
        var t = Whitfields();
        var doc = Narrative.PlaceList(t);
        var table = doc.Blocks.OfType<Table>().Single();
        var names = table.Rows.Select(r => r[0].Text).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names);
        var leeds = table.Rows.Single(r => r[0].Text == "Leeds, Yorkshire, England");
        Assert.Equal(new[] { "4", "4", "1845–1874" }, leeds.Skip(1).Select(c => c.Text));   // Eliza, Clara, Sam, and the marriage
        Assert.Equal("1898", table.Rows.Single(r => r[0].Text.StartsWith("Boston"))[3].Text);
    }

    [Fact]
    public void SurnameListCountsPeopleAndYears()
    {
        var t = Whitfields();
        t.AddPerson(new Person());                                     // no name at all
        var table = Narrative.SurnameList(t).Blocks.OfType<Table>().Single();
        Assert.Equal(new[] { "Cole", "Hartley", "Moss", "Pryor", "Whitfield", "(no surname)" }, table.Rows.Select(r => r[0].Text));
        var whitfield = table.Rows.Single(r => r[0].Text == "Whitfield");
        Assert.Equal("5", whitfield[1].Text);
        Assert.Equal("1815–1911", whitfield[2].Text);
    }

    [Fact]
    public void CalendarListsFullDatesMonthByMonth()
    {
        var t = Whitfields();
        var doc = Narrative.BirthdayCalendar(t);
        Assert.Equal(new[] { "February", "March", "April", "May", "June", "November" }, doc.Blocks.OfType<Heading>().Select(h => h.Text));
        var lines = doc.Blocks.OfType<BulletList>().SelectMany(l => l.Items).Select(i => Run.Join(i.Runs)).ToList();
        Assert.Contains("14 March — Thomas Edward Whitfield born 1842", lines);
        Assert.Contains("18 June — Thomas Edward Whitfield and Eliza Hartley married 1868", lines);
        Assert.Contains("9 November — Thomas Edward Whitfield died 1911", lines);
        Assert.DoesNotContain(lines, l => l.Contains("George"));      // "about 1815" has no day or month
        Assert.DoesNotContain(lines, l => l.Contains("Clara"));       // year only
    }

    [Fact]
    public void FamilyBookHasPartsContentsSourcesAndAnIndex()
    {
        var t = Whitfields();
        var doc = Narrative.FamilyBook(t, t.People[5], new FamilyBookOptions { AncestorGenerations = 3, DescendantGenerations = 2, Author = "A. Cousin" });
        Assert.True(doc.IsBook);
        Assert.Equal("The Whitfield Family", doc.Title);
        Assert.Equal("A. Cousin", doc.Author);
        Assert.Equal(new[] { "introduction", "ancestors", "descendants", "sources" },
                     doc.Blocks.OfType<Heading>().Where(h => h.Level == 1).Select(h => h.Anchor));
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Text == FamilyBookOptions.PlaceholderIntroduction);
        Assert.Contains(doc.Contents, h => h.Anchor == "a-gen-2");

        Assert.StartsWith("2. Thomas Edward Whitfield was born on 14 March 1842", Entry(doc, "a-2"));
        Assert.Contains("the son of George Whitfield (4) and Mary Ann Cole (5)", Entry(doc, "a-2"));
        Assert.Contains("He married Eliza Hartley (3) on 18 June 1868", Entry(doc, "a-2"));
        Assert.StartsWith("1. Clara Whitfield was born in 1872", Entry(doc, "d-1"));

        var bibliography = doc.Blocks.OfType<BulletList>().Last().Items.Select(i => Run.Join(i.Runs)).ToList();
        Assert.Equal(new[] { "Church of England. Parish registers of St Mary, Harrow.", "Leeds marriages. Leeds, 1868." }, bibliography);

        var thomas = Assert.Single(doc.Index, e => e.Name == "Whitfield, Thomas Edward (1842–1911)");
        Assert.Equal(new[] { "A1", "A2", "A3", "D1" }, thomas.Refs.Select(r => r.Label));
        Assert.Contains(doc.Index, e => e.Name == "Pryor, Alice (1898–)");
        var anchors = Anchors(doc);
        Assert.All(doc.Index.SelectMany(e => e.Refs), r => Assert.Contains(r.Anchor, anchors));
        Assert.All(AllRuns(doc).Where(r => r.Link != null), r => Assert.Contains(r.Link!, anchors));
    }

    [Fact]
    public void FamilyBookCanLeaveOutSourcesAndIndex()
    {
        var t = Whitfields();
        var doc = Narrative.FamilyBook(t, t.People[1], new FamilyBookOptions { IncludeSources = false, IncludeIndex = false, Introduction = "Hello.\n\nSecond paragraph." });
        Assert.Empty(doc.Notes);
        Assert.Empty(doc.Index);
        Assert.DoesNotContain(doc.Blocks.OfType<Heading>(), h => h.Anchor == "sources");
        Assert.DoesNotContain(AllRuns(doc), r => r.NoteNumber != null);
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Text == "Second paragraph.");
    }

    [Fact]
    public void LifeSummaryHasNoNoteMarkers()
    {
        var t = Whitfields();
        var summary = Narrative.LifeSummary(t, t.People[1]);
        Assert.StartsWith("Thomas Edward Whitfield was born on 14 March 1842 in Harrow, Middlesex, England, the son of", summary);
        Assert.DoesNotContain("[", summary);
        var nobody = t.AddPerson(new Person());
        Assert.Equal("(no name).", Narrative.LifeSummary(t, nobody));
    }
}
