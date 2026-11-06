using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Reports;

namespace Lineage.App.Tests;

public class SectionTabTests
{
    [AvaloniaFact]
    public async Task ChartsHaveTheirOwnTab()
    {
        var (vm, _) = await Demo.OpenAsync();
        Assert.Equal(PersonView.Details, vm.View);

        vm.Section = AppSection.Charts;
        Assert.Equal(PersonView.Pedigree, vm.View);         // the Charts tab opens on a chart
        Assert.True(vm.IsPersonShown && vm.IsPeopleList);  // with the person and the people list beside it
        vm.ChartTabIndex = 1;
        Assert.Equal(PersonView.FanChart, vm.View);

        vm.NavigateCommand.Execute(2L);                     // clicking someone in a chart centres them
        Assert.Equal(AppSection.Charts, vm.Section);
        Assert.Equal("George Whitfield", vm.CurrentName);

        vm.Section = AppSection.People;                     // back on People: their page, not a chart
        Assert.Equal(PersonView.Details, vm.View);
        vm.Section = AppSection.Charts;
        Assert.Equal(PersonView.FanChart, vm.View);         // the last chart used

        vm.ChartTabIndex = 4;
        Assert.Equal(PersonView.BowTie, vm.View);
        Assert.True(vm.HasGenerations);
        foreach (var (index, view) in new[] { (5, PersonView.Lifelines), (6, PersonView.Relationship), (7, PersonView.Statistics) })
        {
            vm.ChartTabIndex = index;
            Assert.Equal(view, vm.View);
            Assert.False(vm.HasGenerations);   // no generations to pick for these
            Assert.Equal(AppSection.Charts, vm.Section);
        }
        vm.ChartTabIndex = 0;
        Assert.True(vm.HasGenerations);

        vm.ShowViewCommand.Execute(PersonView.Timeline);    // Ctrl+7
        Assert.Equal(AppSection.People, vm.Section);
        vm.ShowViewCommand.Execute(PersonView.Hourglass);   // Ctrl+5
        Assert.Equal(AppSection.Charts, vm.Section);
    }

    [AvaloniaFact]
    public async Task ReportsAndResearchCanBePrinted()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.Section = AppSection.Reports;
        Assert.True(vm.IsReportsSection);
        Assert.False(vm.HasSidebar);
        Assert.True(vm.FamilyBookCommand.CanExecute(null));

        vm.Section = AppSection.Research;
        await Task.Delay(300);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("", vm.ProblemsSummary);

        var path = Path.Combine(Path.GetTempPath(), $"lineage-research-{Guid.NewGuid():N}.csv");
        dialogs.SavePath = path;
        vm.Tree.AddResearch(new Lineage.Core.Model.ResearchItem { Description = "Find the will, \"probate\" index" });
        await vm.ExportResearchCsvCommand.ExecuteAsync(null);
        var text = await File.ReadAllTextAsync(path);
        File.Delete(path);
        Assert.StartsWith("Task or search,About,", text.TrimStart('\uFEFF'));
        Assert.Contains("\"Find the will, \"\"probate\"\" index\"", text);   // commas and quotes kept safe

        var doc = Narrative.ResearchLog(vm.Tree);
        Assert.Equal("Research log", doc.Title);
    }
}
