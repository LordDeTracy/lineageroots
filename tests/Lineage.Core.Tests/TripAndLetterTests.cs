using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using Lineage.Core.Reports;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class TripAndLetterTests
{
    [Fact]
    public void ATripPlanGathersWhatToDoAtOneArchive()
    {
        var t = Whitfields();
        var archive = t.AddRepository(new Repository { Name = "West Yorkshire Archive Service", Address = "Morley, Leeds" });
        var other = t.AddRepository(new Repository { Name = "Somewhere Else" });
        var marriages = t.Sources.Values.Single(s => s.Title == "Leeds marriages");
        marriages.RepositoryId = archive.Id;
        marriages.CallNumber = "RDP68";
        var task = t.AddResearch(new ResearchItem { Description = "Look for Clara's baptism", RepositoryId = archive.Id, Priority = 1 });
        t.People[5].ResearchIds.Add(task.Id);
        t.AddResearch(new ResearchItem { Description = "Banns book", SourceId = marriages.Id });
        t.AddResearch(new ResearchItem { Description = "Already done", RepositoryId = archive.Id, Done = true });
        t.AddResearch(new ResearchItem { Description = "Elsewhere", RepositoryId = other.Id });
        t.AddResearch(new ResearchItem { Description = "Asked for a search", Letter = new Correspondence { With = "West Yorkshire Archive Service", Method = "Email" } });

        var doc = Narrative.TripPlan(t, archive);
        Assert.Equal("Visit to West Yorkshire Archive Service", doc.Title);
        var todo = doc.Blocks.OfType<BulletList>().First().Items.Select(i => string.Concat(i.Runs.Select(r => r.Text))).ToList();
        Assert.Equal(2, todo.Count);
        Assert.StartsWith("☐  Look for Clara's baptism — Clara Whitfield", todo[0]);          // high priority first
        Assert.Contains(todo, x => x.Contains("Banns book") && x.Contains("Leeds marriages"));
        var held = Assert.Single(doc.Blocks.OfType<Table>()).Rows.Single();
        Assert.Equal("RDP68", string.Concat(held[1].Runs.Select(r => r.Text)));
        Assert.Equal("Thomas Edward Whitfield, Eliza Hartley", string.Concat(held[2].Runs.Select(r => r.Text)));   // their marriage cites it
        Assert.Contains(doc.Blocks.OfType<Heading>(), h => h.Text == "Letters waiting for a reply");
    }

    [Fact]
    public void LettersSurviveGedcom()
    {
        var t = Whitfields();
        t.AddResearch(new ResearchItem
        {
            Description = "Asked for Thomas's death certificate", Date = GenDate.Parse("3 MAR 2026"), Done = true,
            Letter = new Correspondence { With = "Massachusetts Registry", Address = "vitals@example.org", Method = "Email", Replied = GenDate.Parse("20 MAR 2026") },
        });
        var path = Path.Combine(Path.GetTempPath(), $"lineage-letter-{Guid.NewGuid():N}.ged");
        try
        {
            GedcomExporter.ExportFile(t, path);
            var back = GedcomImporter.ImportFile(path).Research.Values.Single(r => r.Letter != null);
            Assert.Equal(("Massachusetts Registry", "vitals@example.org", "Email", false, 2026), (back.Letter!.With, back.Letter.Address, back.Letter.Method, back.Letter.Incoming, back.Letter.Replied.Year));
            Assert.True(back.Done);
        }
        finally { File.Delete(path); }
    }
}
