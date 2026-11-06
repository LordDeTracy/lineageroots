using Avalonia.Headless.XUnit;

namespace Lineage.App.Tests;

public class MoreReportTests
{
    [AvaloniaFact]
    public async Task TheNewReportsArePreviewedAndSaved()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var commands = new[] { vm.NgsqReportCommand, vm.AhnentafelReportCommand, vm.WhosMissingReportCommand, vm.BibliographyReportCommand };
        foreach (var command in commands)
        {
            var path = Path.Combine(Path.GetTempPath(), $"lineage-report-{Guid.NewGuid():N}.pdf");
            try
            {
                dialogs.SavePath = path;
                Assert.True(command.CanExecute(null));
                await command.ExecuteAsync(null);
                Assert.True(File.Exists(path), command.ToString());
            }
            finally { File.Delete(path); }
        }
        Assert.Equal(commands.Length, dialogs.Previews.Count);
    }
}
