using Avalonia;
using Avalonia.Headless.XUnit;
using Lineage.App.Controls;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

/// <summary>Drives the main window's view model the way a user would, with fake dialogs.</summary>
public class WorkflowTests
{
    [AvaloniaFact]
    public async Task AddingAnEventCanBeUndoneAndRedone()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var me = vm.Tree.People[1];
        Assert.Equal("Thomas Edward Whitfield", vm.CurrentName);
        Assert.False(vm.UndoCommand.CanExecute(null));

        dialogs.Event = editor =>
        {
            editor.Type = EventEditorViewModel.PersonTypes.First(t => t.Tag == "OCCU");
            editor.Value = "Stonemason";
            editor.Date = "1871";
            editor.Place = "Leeds";
            editor.Coordinates = "53.8008, -1.5491";
            return EditorResult.Saved;
        };
        await vm.AddEventCommand.ExecuteAsync(null);
        Assert.Contains(me.Events, e => e.Tag == "OCCU" && e.Value == "Stonemason" && e.Location != null);
        Assert.Contains(vm.Events, r => r.Label == "Occupation");
        Assert.Equal("Undo add occupation", vm.UndoText);

        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Tree.People[1].Events, e => e.Tag == "OCCU");
        Assert.DoesNotContain(vm.Events, r => r.Label == "Occupation");

        vm.RedoCommand.Execute(null);
        Assert.Contains(vm.Tree.People[1].Events, e => e.Tag == "OCCU");
    }

    [AvaloniaFact]
    public async Task TimelineIncludesMarriageAndEditsIt()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var marriage = Assert.Single(vm.Events, r => r.Label.StartsWith("Marriage"));
        Assert.Equal("Marriage — Eliza Hartley", marriage.Label);
        Assert.IsType<Family>(marriage.Owner);

        dialogs.Event = editor => { editor.Place = "Leeds Parish Church"; return EditorResult.Saved; };
        await vm.EditEventCommand.ExecuteAsync(marriage);
        Assert.Equal("Leeds Parish Church", vm.Tree.Families[2].Marriage!.Place);

        dialogs.Event = _ => EditorResult.Deleted;
        await vm.EditEventCommand.ExecuteAsync(vm.Events.Single(r => r.Label.StartsWith("Marriage")));
        Assert.Null(vm.Tree.Families[2].Marriage);
        vm.UndoCommand.Execute(null);
        Assert.NotNull(vm.Tree.Families[2].Marriage);
    }

    [AvaloniaFact]
    public async Task CitingANewSourceCreatesItAndDeletingASourceRemovesItsCitations()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var birth = vm.Events.Single(r => r.Label == "Birth");
        dialogs.Event = editor =>
        {
            Assert.Single(editor.Citations);                    // the existing parish register citation
            editor.NewSourceTitle = "Whitfield family Bible";
            editor.NewPage = "Front cover";
            editor.AddCitationCommand.Execute(null);
            return EditorResult.Saved;
        };
        await vm.EditEventCommand.ExecuteAsync(birth);
        Assert.Equal(2, vm.Tree.Sources.Count);
        Assert.Equal(2, vm.Tree.People[1].Birth!.Citations.Count);

        vm.Section = AppSection.Sources;
        Assert.Equal(2, vm.SourceList.Count);
        vm.SelectedSource = vm.SourceList.Single(s => s.Title == "Parish registers of St Mary, Harrow");
        Assert.Single(vm.CitedBy);
        await vm.DeleteSourceCommand.ExecuteAsync(null);
        Assert.Single(vm.Tree.Sources);
        Assert.Single(vm.Tree.People[1].Birth!.Citations);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Tree.Sources.Count);
        Assert.Equal(2, vm.Tree.People[1].Birth!.Citations.Count);
    }

    [AvaloniaFact]
    public async Task EditingASourceAndItsRepository()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.Section = AppSection.Sources;
        dialogs.Source = editor =>
        {
            editor.Author = "Church of England";
            editor.Repository = editor.Repositories.Single(r => r.Id == -1);  // "New repository…"
            editor.RepositoryName = "London Metropolitan Archives";
            return EditorResult.Saved;
        };
        await vm.EditSourceCommand.ExecuteAsync(null);
        var s = vm.Tree.Sources[1];
        Assert.Equal("Church of England", s.Author);
        Assert.Equal("London Metropolitan Archives", vm.Tree.Repositories[s.RepositoryId!.Value].Name);
        Assert.Contains("London Metropolitan Archives", vm.SourceRepository);
    }

    [AvaloniaFact]
    public async Task MergingDuplicatesIsUndoable()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        DuplicatesViewModel? seen = null;
        dialogs.Duplicates = d =>
        {
            seen = d;
            var pair = Assert.Single(d.Pairs);
            Assert.Equal((1, 6), (pair.Match.A.Id, pair.Match.B.Id));
            Assert.Equal(0, d.KeepIndex);                       // the record with more information
            d.MergeCommand.Execute(null);
        };
        await vm.FindDuplicatesCommand.ExecuteAsync(null);
        Assert.NotNull(seen);
        Assert.True(seen!.IsEmpty);
        Assert.Equal(5, vm.Tree.People.Count);
        Assert.Contains(vm.Tree.People[1].Names, n => n.Full == "Thomas Whitfeld");

        vm.UndoCommand.Execute(null);
        Assert.Equal(6, vm.Tree.People.Count);
    }

    [AvaloniaFact]
    public async Task NotADuplicateIsRemembered()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Duplicates = d => d.NotDuplicateCommand.Execute(null);
        await vm.FindDuplicatesCommand.ExecuteAsync(null);
        Assert.Contains("1:6", vm.Tree.NotDuplicates);
        var count = -1;
        dialogs.Duplicates = d => count = d.Pairs.Count;
        await vm.FindDuplicatesCommand.ExecuteAsync(null);
        Assert.Equal(0, count);
    }

    [AvaloniaFact]
    public async Task GpsTypedOnThePlacesCardIsSavedAndUndoable()
    {
        var (vm, _) = await Demo.OpenAsync();
        var harrow = vm.Places.Single(p => p.Place == "Harrow, Middlesex, England");
        harrow.Coordinates = "N51.5791 W0.3368";
        Assert.False(harrow.HasError);
        Assert.Equal(51.5791, vm.Tree.People[1].Birth!.Location!.Value.Latitude, 4);
        Assert.True(vm.Events.Single(r => r.Label == "Birth").HasGps);
        harrow.Coordinates = "not a place";
        Assert.True(harrow.HasError);
        Assert.NotNull(vm.Tree.People[1].Birth!.Location);      // a bad entry doesn't wipe the good one

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.Tree.People[1].Birth!.Location);
    }

    [AvaloniaFact]
    public async Task ReportsAreWrittenAsPdf()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        foreach (var command in new[] { vm.FamilyGroupReportCommand, vm.AncestorReportCommand, vm.DescendantReportCommand })
        {
            var path = Path.Combine(Path.GetTempPath(), $"lineage-report-{Guid.NewGuid():N}.pdf");
            dialogs.SavePath = path;
            await command.ExecuteAsync(null);
            Assert.Empty(dialogs.Messages);
            Assert.Contains(path, TestApp.Opened);                 // the app offers the finished PDF
            var head = new byte[5];
            await using (var f = File.OpenRead(path)) _ = await f.ReadAsync(head);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
            Assert.True(new FileInfo(path).Length > 2000);
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task ChartsMeasureAndSaveAsImages()
    {
        var (vm, _) = await Demo.OpenAsync();
        Assert.NotNull(vm.Chart);
        Assert.Equal(new[] { 1, 2, 3 }, vm.Chart!.Ancestors.Keys.Order());
        foreach (ChartBase chart in new ChartBase[] { new PedigreeChart { Generations = 4 }, new FanChart { Generations = 5 } })
        {
            chart.Data = vm.Chart;
            chart.Measure(Size.Infinity);
            Assert.True(chart.DesiredSize.Width > 400 && chart.DesiredSize.Height > 300);
            chart.Arrange(new Rect(chart.DesiredSize));
            var path = Path.Combine(Path.GetTempPath(), $"lineage-chart-{Guid.NewGuid():N}.png");
            chart.SaveImage(path);
            Assert.True(new FileInfo(path).Length > 5000);
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task DeletingAPersonCanBeUndone()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.NavigateCommand.Execute(5L);
        Assert.Equal("Clara Whitfield", vm.CurrentName);
        await vm.DeletePersonCommand.ExecuteAsync(null);
        Assert.Null(vm.Tree.Person(5));
        Assert.DoesNotContain(vm.Tree.Families[2].Children, c => c.ChildId == 5);
        vm.UndoCommand.Execute(null);
        Assert.NotNull(vm.Tree.Person(5));
        Assert.Contains(vm.Tree.Families[2].Children, c => c.ChildId == 5);
    }
}
