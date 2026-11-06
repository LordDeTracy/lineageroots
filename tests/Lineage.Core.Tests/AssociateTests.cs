using Lineage.Core.Analysis;
using Lineage.Core.Editing;
using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class AssociateTests
{
    /// <summary>Whitfields plus a neighbour who witnessed Thomas and Eliza's marriage and stood godparent to Clara.</summary>
    private static (FamilyTree Tree, Person Neighbour) Tree()
    {
        var t = Whitfields();
        var ann = P(t, "Ann", "Bell", Sex.Female, E("BIRT", "1840"));
        var marriage = t.Families.Values.Single(f => f.Partner1Id == 1).Events.Single(e => e.Tag == "MARR");
        marriage.Roles.Add(new EventRole { PersonId = ann.Id, Role = "Witness" });
        t.People[5].Events.Single(e => e.Tag == "BIRT").Roles.Add(new EventRole { PersonId = ann.Id, Role = "Godparent" });
        t.People[1].Associations.Add(new Association { PersonId = ann.Id, Relation = "Neighbour", Note = "Lived next door in Leeds" });
        return (t, ann);
    }

    [Fact]
    public void BothSidesSeeTheLink()
    {
        var (t, ann) = Tree();
        var hers = Associates.Of(t, ann);
        Assert.Contains(hers, a => a.Other.Id == 1 && a.How == "Neighbour" && a.Theirs);
        Assert.Contains(hers, a => a.How == "Ann Bell was witness at the marriage of Thomas Edward Whitfield and Eliza Hartley" && a.Year == 1868);
        Assert.Contains(hers, a => a.How.StartsWith("Ann Bell was godparent at the birth of Clara Whitfield"));

        var his = Associates.Of(t, t.People[1]);
        Assert.Contains(his, a => a.Other == ann && a.How == "Neighbour" && !a.Theirs);
        Assert.Contains(his, a => a.Other == ann && a.How == "Witness at the couple’s marriage");
        Assert.Contains(Associates.Of(t, t.People[5]), a => a.Other == ann && a.How == "Godparent at their birth");
    }

    [Fact]
    public void RolesAndAssociationsSurviveGedcom()
    {
        var (t, ann) = Tree();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-asso-{Guid.NewGuid():N}.ged");
        try
        {
            GedcomExporter.ExportFile(t, path);
            var text = File.ReadAllText(path);
            Assert.Contains("1 ASSO @I" + ann.Id + "@", text);
            Assert.Contains("3 RELA Witness", text);
            var back = GedcomImporter.ImportFile(path);
            var ann2 = back.People.Values.Single(p => p.DisplayName == "Ann Bell");
            var thomas = back.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
            var a = Assert.Single(thomas.Associations);
            Assert.Equal((ann2.Id, "Neighbour", "Lived next door in Leeds"), (a.PersonId, a.Relation, a.Note));
            var marriage = back.Families.Values.SelectMany(f => f.Events).Single(e => e.Tag == "MARR" && e.Date.Year == 1868);
            Assert.Equal((ann2.Id, "Witness"), (marriage.Roles[0].PersonId, marriage.Roles[0].Role));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMergeMovesTheLinks()
    {
        var (t, ann) = Tree();
        var copy = P(t, "Ann", "Bell", Sex.Female);
        t.People[6].Associations.Add(new Association { PersonId = copy.Id, Relation = "Friend" });
        Merger.Merge(t, ann, copy);
        Assert.Equal(ann.Id, t.People[6].Associations[0].PersonId);

        // A deleted person's links are just skipped.
        t.RemovePerson(ann);
        Assert.Empty(Associates.Of(t, t.People[1]));
    }

    [Fact]
    public void GedcomSevenRoleWordsAreRead()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lineage-asso-{Guid.NewGuid():N}.ged");
        File.WriteAllText(path, "0 HEAD\n1 CHAR UTF-8\n0 @I1@ INDI\n1 NAME Ann /Bell/\n0 @I2@ INDI\n1 NAME Tom /Bell/\n1 BAPM\n2 DATE 1850\n2 ASSO @I1@\n3 ROLE GODP\n0 TRLR\n");
        try
        {
            var t = GedcomImporter.ImportFile(path);
            var tom = t.People.Values.Single(p => p.DisplayName == "Tom Bell");
            Assert.Equal("Godparent", tom.Events[0].Roles[0].Role);
        }
        finally { File.Delete(path); }
    }
}
