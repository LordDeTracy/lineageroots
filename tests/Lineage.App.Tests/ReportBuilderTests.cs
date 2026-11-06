using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Export;

namespace Lineage.App.Tests;

public class ReportBuilderTests
{
    [AvaloniaFact]
    public async Task TheBuilderCountsPeopleAndMakesTheReport()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        vm.NavigateCommand.Execute(thomas.Id);

        var builder = new ReportBuilderViewModel(vm.Tree, thomas);
        Assert.Equal("3 people", builder.CountText);                     // Thomas and his parents
        builder.PeopleIndex = 0;
        Assert.Equal("1 person", builder.CountText);
        Assert.False(builder.HasGenerations);
        Assert.Equal(BranchKind.Ancestors, builder.ToOptions(null).People);
        Assert.Equal(1, builder.ToOptions(null).Generations);
        builder.PeopleIndex = 4;
        Assert.Equal($"{vm.Tree.People.Count} people", builder.CountText);

        var path = Path.Combine(Path.GetTempPath(), $"lineage-built-{Guid.NewGuid():N}.pdf");
        try
        {
            dialogs.SavePath = path;
            dialogs.ReportBuilder = b => { b.PeopleIndex = 2; b.Title = "Our family"; b.Index = true; return true; };
            await vm.BuildReportCommand.ExecuteAsync(null);
            var preview = Assert.Single(dialogs.Previews);
            Assert.NotEmpty(preview.PageImages);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
