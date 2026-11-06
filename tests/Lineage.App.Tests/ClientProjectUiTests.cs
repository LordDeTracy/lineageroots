using Avalonia.Headless.XUnit;

namespace Lineage.App.Tests;

public class ClientProjectUiTests
{
    [AvaloniaFact]
    public async Task AdvancedModeAddsClientProjectsWithInvoices()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        Assert.False(vm.IsAdvanced);
        vm.Settings.AdvancedMode = true;
        vm.ResearchFilterIndex = 6;
        Assert.Equal("New client project", vm.NewResearchText);

        var path = Path.Combine(Path.GetTempPath(), $"lineage-invoice-{Guid.NewGuid():N}.pdf");
        try
        {
            dialogs.SavePath = path;
            dialogs.Project = e =>
            {
                e.Client = "Mrs Pryor";
                e.Rate = "40";
                e.AddTimeCommand.Execute(null);
                e.Time[0].Hours = "2.5";
                e.Time[0].Work = "Parish registers";
                Assert.Contains("100.00 not yet invoiced", e.TotalsText);
                e.InvoiceUnbilledCommand.Execute(null);
                Assert.Single(e.Invoices);
                Assert.True(e.Time[0].Billed);
                return true;
            };
            await vm.NewResearchCommand.ExecuteAsync(null);
            var row = Assert.Single(vm.ProjectRows);
            Assert.Equal("Mrs Pryor", row.Client);
            Assert.Contains("$100.00 awaiting payment", row.Detail);
            Assert.True(File.Exists(path));                                  // the invoice was saved as a document
            Assert.Single(vm.Tree.Projects[0].Invoices);
        }
        finally { File.Delete(path); }
    }
}
