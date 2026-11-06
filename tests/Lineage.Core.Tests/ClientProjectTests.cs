using Lineage.Core.Model;
using Lineage.Core.Reports;
using Lineage.Core.Storage;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class ClientProjectTests
{
    private static ClientProject Project() => new()
    {
        Client = "Mrs Pryor", Question = "Who were Thomas Whitfield's parents?", Rate = 40, Currency = "£", BudgetHours = 10,
        Time =
        {
            new TimeEntry { Date = GenDate.Parse("3 MAR 2026"), Hours = 2.5, Work = "Parish registers, Harrow" },
            new TimeEntry { Date = GenDate.Parse("10 MAR 2026"), Hours = 1.5, Work = "1851 census" },
        },
        Deliverables = { new Deliverable { What = "Written report", Due = GenDate.Parse("1 APR 2026") } },
    };

    [Fact]
    public void UnbilledHoursBecomeAnInvoice()
    {
        var p = Project();
        Assert.Equal(160m, p.Unbilled);
        var invoice = p.InvoiceUnbilled(new DateTime(2026, 3, 15), "INV-2026-001")!;
        Assert.Equal(160m, invoice.Amount);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(0m, p.Unbilled);
        Assert.Equal(160m, p.Outstanding);
        Assert.Null(p.InvoiceUnbilled(new DateTime(2026, 3, 16), "INV-2026-002"));   // nothing left to bill

        var doc = Narrative.InvoiceDocument(p, invoice, "A. Researcher");
        Assert.Equal("Invoice INV-2026-001", doc.Title);
        var total = doc.Blocks.OfType<Table>().Single().Rows.Last();
        Assert.Equal("£160.00", string.Concat(total[3].Runs.Select(r => r.Text)));
    }

    [Fact]
    public void ProjectsAreSavedWithTheTree()
    {
        var t = Whitfields();
        var p = Project();
        p.PersonIds.Add(1);
        t.Projects.Add(p);
        var path = Path.Combine(Path.GetTempPath(), $"lineage-projects-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(t, path);
            var back = Assert.Single(LineageStore.Load(path).Projects);
            Assert.Equal(("Mrs Pryor", 40m, "£", 2, 4.0), (back.Client, back.Rate, back.Currency, back.Time.Count, back.Hours));
            Assert.Equal("Written report", back.Deliverables[0].What);
            Assert.Equal(new long[] { 1 }, back.PersonIds);
        }
        finally { File.Delete(path); }
    }
}
