using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class Gedcom7Tests
{
    private static FamilyTree Tree()
    {
        var t = Whitfields();
        var thomas = t.People[1];
        thomas.FamilySearchId = "KWCB-123";
        thomas.Note = "A long note. " + string.Concat(Enumerable.Repeat("He worked stone all his life. ", 20)) + "\nSecond line.";
        var photo = new MediaLink { File = "photos/thomas.jpg", Title = "Thomas at work", Primary = true, Private = true };
        photo.Regions.Add(new PhotoRegion { PersonId = 1, X = 0.1, Y = 0.1, Width = 0.3, Height = 0.4 });
        thomas.Media.Add(photo);
        thomas.Associations.Add(new Association { PersonId = 9, Relation = "Business partner" });
        t.Families.Values.Single(f => f.Partner1Id == 1).Events.Single(e => e.Tag == "MARR").Roles.Add(new EventRole { PersonId = 8, Role = "Witness" });
        thomas.Names[1].Type = "married";
        thomas.Events.Add(new Event { Tag = "RESI", Date = GenDate.Parse("14 MAR 1741/42"), Place = "Leeds" });
        var link = t.Families.Values.Single(f => f.Partner1Id == 1).Children.Single(c => c.ChildId == 6);
        link.ToFather = link.ToMother = ParentRelation.Adopted;
        return t;
    }

    [Fact]
    public void TheFileFollowsVersionSeven()
    {
        var text = Gedcom7.Export(Tree());
        Assert.Contains("1 GEDC\n2 VERS 7.0\n", text);
        Assert.DoesNotContain(" CONC ", text);
        Assert.DoesNotContain("1 CHAR", text);
        Assert.DoesNotContain(" RELA ", text);
        Assert.DoesNotContain(" RIN ", text);
        Assert.Contains("0 @O1@ OBJE\n1 FILE photos/thomas.jpg\n2 FORM image/jpeg\n2 TITL Thomas at work\n", text);
        Assert.Contains("1 OBJE @O1@\n2 _PRIM Y\n", text);
        Assert.Contains("3 ROLE WITN\n", text);                       // under the marriage's ASSO
        Assert.Contains("2 ROLE OTHER\n3 PHRASE Business partner\n", text);
        Assert.Contains("2 TYPE MARRIED\n", text);
        Assert.Contains("2 PEDI ADOPTED\n", text);
        Assert.Contains("1 EXID KWCB-123\n2 TYPE " + Gedcom7.FamilySearchExid, text);
        Assert.Contains("2 DATE 14 MAR 1742\n3 PHRASE 14 MAR 1741/1742\n", text);
        Assert.EndsWith("0 TRLR\n", text);
    }

    [Fact]
    public void ItReadsBackTheSame()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lineage-g7-{Guid.NewGuid():N}.ged");
        try
        {
            Gedcom7.ExportFile(Tree(), path);
            var back = GedcomImporter.ImportFile(path);
            Assert.Equal(10, back.People.Count);
            var thomas = back.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
            Assert.Equal("KWCB-123", thomas.FamilySearchId);
            Assert.EndsWith("\nSecond line.", thomas.Note);
            var photo = Assert.Single(thomas.Media);
            Assert.Equal(("photos/thomas.jpg", "Thomas at work", true, true), (photo.File, photo.Title, photo.Primary, photo.Private));
            Assert.Single(photo.Regions);
            Assert.Equal("Business partner", Assert.Single(thomas.Associations).Relation);
            Assert.Equal("married", thomas.Names[1].Type);
            Assert.Equal("Witness", back.Families.Values.SelectMany(f => f.Events).Single(e => e.Tag == "MARR" && e.Date.Year == 1868).Roles[0].Role);
            var sam = back.People.Values.Single(p => p.DisplayName == "Sam Whitfield");
            var link = back.Families.Values.SelectMany(f => f.Children).Single(c => c.ChildId == sam.Id);
            Assert.Equal(ParentRelation.Adopted, link.ToFather);
            Assert.Equal(1742, thomas.Events.Single(e => e.Tag == "RESI" && e.Place == "Leeds").Date.Year);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SharedNotesAndVoidPointersFromOtherPrograms()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lineage-g7-{Guid.NewGuid():N}.ged");
        File.WriteAllText(path, "0 HEAD\n1 GEDC\n2 VERS 7.0\n0 @I1@ INDI\n1 NAME Ann /Bell/\n1 SNOTE @N1@\n1 FAMC @VOID@\n1 ASSO @VOID@\n2 ROLE FRIEND\n0 @N1@ SNOTE Seen in the 1851 census\n0 TRLR\n");
        try
        {
            var ann = GedcomImporter.ImportFile(path).People.Values.Single();
            Assert.Equal("Seen in the 1851 census", ann.Note);
            Assert.Empty(ann.Associations);
            Assert.Empty(ann.ParentFamilyIds);
        }
        finally { File.Delete(path); }
    }
}
