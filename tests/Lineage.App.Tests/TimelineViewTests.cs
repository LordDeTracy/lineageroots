using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class TimelineViewTests
{
    [AvaloniaFact]
    public async Task TimelineShowsLifeFamilyAndHistory()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.View = PersonView.Timeline;
        Assert.True(vm.IsTimelineView);
        Assert.False(vm.IsChartView);

        var titles = vm.TimelineRows.Select(r => r.Title).ToList();
        Assert.Equal("Birth", titles[0]);
        Assert.Contains("Marriage to Eliza Hartley", titles);
        Assert.Contains("Birth of daughter, Clara Whitfield", titles);
        Assert.Contains("Wills proved in civil courts", titles);   // England, 1858
        Assert.DoesNotContain("American Civil War", titles);
        Assert.Equal("Death", titles[^1]);
        Assert.Contains("Thomas Edward Whitfield's life (1842–1911)", vm.TimelineText);

        vm.ShowHistoryOnTimeline = false;
        Assert.DoesNotContain(vm.TimelineRows, r => r.IsHistory);
        vm.ShowFamilyOnTimeline = false;
        Assert.DoesNotContain(vm.TimelineRows, r => r.IsFamily);
        vm.ShowFamilyOnTimeline = true;

        // Clicking a relative's event moves to their timeline.
        await vm.OpenTimelineEntryCommand.ExecuteAsync(vm.TimelineRows.First(r => r.Title.StartsWith("Birth of daughter")));
        Assert.Equal("Clara Whitfield", vm.CurrentName);
        Assert.True(vm.IsTimelineView);
    }

    [AvaloniaFact]
    public async Task CensusChecklistCanBeTurnedIntoTasksAndRecords()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.View = PersonView.Timeline;

        // Born 1842 in Harrow, married 1868 in Leeds, died November 1911: every England and Wales census 1851–1911.
        Assert.Equal(new[] { 1851, 1861, 1871, 1881, 1891, 1901, 1911 }, vm.CensusRows.Select(r => r.Check.Census.Year));
        Assert.All(vm.CensusRows, r => Assert.True(r.IsToFind));
        Assert.Equal("0 of 7 found", vm.CensusSummary);
        var row1881 = vm.CensusRows.Single(r => r.Check.Census.Year == 1881);
        Assert.Equal("3 Apr 1881", row1881.Date);
        Assert.Equal("Leeds, Yorkshire, England", row1881.Check.Place);

        vm.AddCensusTaskCommand.Execute(row1881);
        var task = Assert.Single(vm.Tree.Research.Values);
        Assert.Equal("Find Thomas Edward Whitfield in the 1881 England and Wales census", task.Description);
        Assert.Equal("Census", task.Category);
        Assert.Contains(task.Id, vm.Tree.People[1].ResearchIds);
        row1881 = vm.CensusRows.Single(r => r.Check.Census.Year == 1881);
        Assert.True(row1881.HasTask);
        Assert.False(row1881.CanAddTask);
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Tree.Research);

        dialogs.Event = editor =>
        {
            Assert.Equal("CENS", editor.Type.Tag);
            return EditorResult.Saved;
        };
        await vm.RecordCensusCommand.ExecuteAsync(vm.CensusRows.Single(r => r.Check.Census.Year == 1881));
        var census = Assert.Single(vm.Tree.People[1].Events, e => e.Tag == "CENS");
        Assert.Equal("3 APR 1881", census.Date.ToGedcom());
        Assert.Equal("Leeds, Yorkshire, England", census.Place);
        Assert.True(vm.CensusRows.Single(r => r.Check.Census.Year == 1881).IsFound);
        Assert.Equal("1 of 7 found", vm.CensusSummary);
        Assert.Contains(vm.TimelineRows, r => r.Title == "Census" && r.IsLife);
    }

    [AvaloniaFact]
    public async Task PeopleWithoutPlacesGetAHelpfulMessage()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.View = PersonView.Timeline;
        vm.NavigateCommand.Execute(5L);   // Clara: born 1872, no place
        Assert.False(vm.HasCensusRows);
        Assert.Contains("add places", vm.CensusEmptyText);
    }
}
