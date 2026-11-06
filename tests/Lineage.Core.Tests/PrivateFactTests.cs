using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using Lineage.Core.Reports;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class PrivateFactTests
{
    private static FamilyTree Tree()
    {
        var t = Whitfields();
        var thomas = t.People[1];
        thomas.NotePrivate = true;                                          // "Thomas carved the font…"
        thomas.Names[1].Private = true;                                     // "Tom Whitfield"
        thomas.Media.Add(new MediaLink { File = "public.jpg", Title = "At work" });
        thomas.Media.Add(new MediaLink { File = "secret.jpg", Title = "Hospital", Private = true });
        return t;
    }

    [Fact]
    public void SharingLeavesPrivateThingsOut()
    {
        var copy = Privacy.Filter(Tree(), new PrivacyOptions { Living = LivingMode.Include, ExcludePrivateEvents = true });
        var thomas = copy.People[1];
        Assert.Equal("", thomas.Note);
        Assert.Single(thomas.Names);
        Assert.Equal("At work", Assert.Single(thomas.Media).Title);

        var kept = Privacy.Filter(Tree(), new PrivacyOptions { Living = LivingMode.Include, ExcludePrivateEvents = false });
        Assert.Equal(2, kept.People[1].Media.Count);                        // only when asked
    }

    [Fact]
    public void ReportsAndTheWebsiteDoNotShowThem()
    {
        var t = Tree();
        var summary = Narrative.IndividualSummary(t, t.People[1]).PlainText();
        Assert.DoesNotContain("Tom Whitfield", summary);
        Assert.DoesNotContain("carved the font", summary);
        var phone = PhoneViewer.Build(t, new PhoneViewerOptions { Picture = (f, _) => "data:image/jpeg;base64," + f });
        Assert.DoesNotContain("secret.jpg", phone);
        Assert.Contains("public.jpg", phone);
        Assert.DoesNotContain("carved the font", phone);
    }

    [Fact]
    public void PrivateMarksSurviveGedcom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lineage-private-{Guid.NewGuid():N}.ged");
        try
        {
            GedcomExporter.ExportFile(Tree(), path);
            var thomas = GedcomImporter.ImportFile(path).People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
            Assert.True(thomas.NotePrivate);
            Assert.True(thomas.Names.Single(n => n.Given == "Tom").Private);
            Assert.True(thomas.Media.Single(m => m.File == "secret.jpg").Private);
            Assert.False(thomas.Media.Single(m => m.File == "public.jpg").Private);
        }
        finally { File.Delete(path); }
    }
}
