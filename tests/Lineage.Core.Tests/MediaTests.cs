using System.Globalization;
using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Media;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Tests;

/// <summary>Photos on events and sources, and people tagged in photos, survive saving and GEDCOM.</summary>
public class MediaTests
{
    /// <summary>The round-trip sample tree plus event media, source media and photo tags.</summary>
    private static FamilyTree Tree()
    {
        var t = RoundTripTests.SampleTree();
        var dad = t.People[1];
        var mum = t.People[2];
        var kid = t.People[3];
        dad.Media[0].Regions.Add(new PhotoRegion { PersonId = dad.Id, X = 0.125, Y = 0.2, Width = 0.15, Height = 0.225 });
        dad.Media[0].Regions.Add(new PhotoRegion { PersonId = kid.Id, X = 0.6, Y = 0.3333, Width = 0.1, Height = 0.12 });
        dad.Birth!.Media.Add(new MediaLink
        {
            File = "Test media/birth certificate.png", Title = "Birth certificate", MediaType = "DOCUMENT", Note = "Copy from the county",
        });
        t.Families[1].Marriage!.Media.Add(new MediaLink
        {
            File = @"C:\Photos\wedding.jpg", Title = "Wedding day", MediaType = "PHOTO", Taken = "7 JUN 1928",
            Location = new GeoPoint(51.5072, -0.1276),
            Regions =
            {
                new PhotoRegion { PersonId = dad.Id, X = 0.1, Y = 0.1, Width = 0.3, Height = 0.4 },
                new PhotoRegion { PersonId = mum.Id, X = 0.5, Y = 0.15, Width = 0.25, Height = 0.4 },
            },
        });
        t.Sources[1].Media.Add(new MediaLink { File = "Test media/register page 12.jpg", Title = "Register, page 12", MediaType = "PHOTO" });
        return t;
    }

    private static string Fingerprint(FamilyTree t) => string.Join("\n", MediaLibrary.AllLinks(t).Select(r =>
        $"{r.Owner.GetType().Name}{r.Owner.Id}|{r.Event?.Tag}|{r.Link.File}|{r.Link.Title}|{r.Link.MediaType}|{r.Link.Note}|" +
        $"{r.Link.Taken}|{r.Link.Primary}|{r.Link.Location}|" +
        string.Join(";", r.Link.Regions.Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.PersonId}@{g.X},{g.Y},{g.Width},{g.Height}")))));

    private static void AssertHasEverything(FamilyTree t)
    {
        Assert.Equal(2, t.People[1].Media[0].Regions.Count);
        Assert.Equal("Birth certificate", Assert.Single(t.People[1].Birth!.Media).Title);
        Assert.Equal(2, Assert.Single(t.Families[1].Marriage!.Media).Regions.Count);
        Assert.Equal("Register, page 12", Assert.Single(t.Sources[1].Media).Title);
    }

    [Fact]
    public void StoreKeepsEventAndSourceMediaAndTags()
    {
        var t = Tree();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(t, path);
            var back = LineageStore.Load(path);
            AssertHasEverything(back);
            Assert.Equal(Fingerprint(t), Fingerprint(back));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UndoSnapshotsKeepTags()
    {
        var p = Tree().People[1];
        var back = LineageStore.FromJson<Person>(LineageStore.ToJson(p));
        Assert.Equal(0.3333, back.Media[0].Regions[1].Y);
        Assert.Equal("Copy from the county", back.Birth!.Media[0].Note);
    }

    [Fact]
    public void GedcomKeepsEventAndSourceMediaAndTags()
    {
        var t = Tree();
        var text = GedcomExporter.Export(t);
        Assert.Contains("1 BIRT\n2 DATE ABT 1900\n", text);
        Assert.Contains("2 OBJE\n3 FILE Test media/birth certificate.png\n", text);
        Assert.Contains("2 _REGION\n3 _PERSON @I3@\n3 _RECT 0.6000 0.3333 0.1000 0.1200\n", text);
        Assert.Contains("1 OBJE\n2 FILE Test media/register page 12.jpg\n", text);
        var back = GedcomImporter.Import(text);
        AssertHasEverything(back);
        Assert.Equal(Fingerprint(t), Fingerprint(back));
    }

    [Fact]
    public void TagRectanglesUseInvariantDecimalsWhateverTheCulture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var text = GedcomExporter.Export(Tree());
            Assert.Contains("_RECT 0.1250 0.2000 0.1500 0.2250", text);
            Assert.Equal(0.225, GedcomImporter.Import(text).People[1].Media[0].Regions[0].Height);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void TagsOfPeopleNoLongerInTheTreeAreLeftOutOfGedcom()
    {
        var t = Tree();
        t.RemovePerson(t.People[3]);
        var text = GedcomExporter.Export(t);
        Assert.DoesNotContain("_PERSON @I3@", text);
        Assert.Equal(1, GedcomImporter.Import(text).People[1].Media[0].Regions[0].PersonId);
    }

    [Fact]
    public void ReadsMediaOnEventsAndSourcesWrittenByHand()
    {
        const string gedcom = """
            0 HEAD
            1 CHAR UTF-8
            0 @I7@ INDI
            1 NAME Ada /Lovell/
            1 BIRT
            2 DATE 1901
            2 OBJE
            3 FILE certificates/ada.jpg
            3 FORM jpg
            3 TITL Birth certificate
            3 _REGION
            4 _PERSON @I7@
            4 _RECT 0.5 0.25 0.1 0.2
            3 _REGION
            4 _PERSON @I99@
            4 _RECT 0.1 0.1 0.1 0.1
            3 _REGION
            4 _PERSON @I7@
            4 _RECT not a rectangle
            0 @S1@ SOUR
            1 TITL Census 1911
            1 OBJE
            2 FILE census/page3.jpg
            0 TRLR
            """;
        var t = GedcomImporter.Import(gedcom);
        var media = Assert.Single(t.People[7].Birth!.Media);
        Assert.Equal(("certificates/ada.jpg", "JPG", "Birth certificate"), (media.File, media.Format, media.Title));
        var tag = Assert.Single(media.Regions);   // unknown person and unreadable rectangle are skipped
        Assert.Equal((7L, 0.5, 0.25, 0.1, 0.2), (tag.PersonId, tag.X, tag.Y, tag.Width, tag.Height));
        Assert.Equal("census/page3.jpg", Assert.Single(t.Sources[1].Media).File);
    }

    [Fact]
    public void CloneIsDeepAndCopyFromWritesBack()
    {
        var original = Tree().Families[1].Marriage!.Media[0];
        var copy = original.Clone();
        copy.Title = "Wedding, 1928";
        copy.Regions[0].X = 0.2;
        copy.Regions.RemoveAt(1);
        Assert.Equal("Wedding day", original.Title);
        Assert.Equal(2, original.Regions.Count);
        Assert.Equal(0.1, original.Regions[0].X);

        original.CopyFrom(copy);
        Assert.Equal("Wedding, 1928", original.Title);
        Assert.Equal(0.2, Assert.Single(original.Regions).X);
        Assert.NotSame(copy.Regions[0], original.Regions[0]);
    }
}
