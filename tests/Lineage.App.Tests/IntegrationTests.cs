using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lineage.App.Controls;
using Lineage.App.ViewModels;
using Lineage.Core.Media;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class IntegrationTests
{
    private static async Task<(Window Window, ZoomHost Host, PedigreeChart Chart, MainViewModel Vm)> ChartInWindowAsync()
    {
        var (vm, _) = await Demo.OpenAsync();
        var chart = new PedigreeChart { Data = vm.Chart, Generations = 4, NavigateCommand = vm.NavigateCommand };
        var host = new ZoomHost { Content = chart };
        var window = new Window { Width = 600, Height = 400, Content = host };
        window.Show();
        window.UpdateLayout();
        return (window, host, chart, vm);
    }

    [AvaloniaFact]
    public async Task MouseWheelZoomsTheChartInsteadOfScrolling()
    {
        var (window, host, _, _) = await ChartInWindowAsync();
        Assert.Equal(1, host.Zoom);
        window.MouseWheel(new Point(300, 200), new Vector(0, 1));
        Assert.True(host.Zoom > 1.1);
        window.MouseWheel(new Point(300, 200), new Vector(0, -1));
        window.MouseWheel(new Point(300, 200), new Vector(0, -1));
        Assert.True(host.Zoom < 1);
        host.ResetZoom();
        Assert.Equal(1, host.Zoom, 3);
        host.FitToView();
        Assert.True(host.Zoom <= 1);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DraggingPansWithoutClickingAPerson()
    {
        var (window, host, _, vm) = await ChartInWindowAsync();
        host.Zoom = 2;
        window.UpdateLayout();
        var before = host.Offset;
        var who = vm.CurrentName;
        window.MouseDown(new Point(300, 200), MouseButton.Left);
        window.MouseMove(new Point(260, 170));
        window.MouseMove(new Point(200, 150));
        window.MouseUp(new Point(200, 150), MouseButton.Left);
        Assert.NotEqual(before, host.Offset);
        Assert.Equal(who, vm.CurrentName);                   // a pan never re-centres the chart
        window.Close();
    }

    [AvaloniaFact]
    public async Task NewViewsHaveTheirOwnGenerationsAndAMap()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.View = PersonView.Descendants;
        Assert.True(vm.IsChartView);
        vm.ChartGenerationsShown = 5;
        Assert.Equal(5, vm.DescendantGenerations);
        vm.View = PersonView.Hourglass;
        vm.ChartGenerationsShown = 4;
        Assert.Equal(4, vm.HourglassGenerations);
        vm.View = PersonView.Map;
        Assert.False(vm.IsChartView);
        Assert.False(vm.HasPersonPins);                      // the demo family has no GPS yet
        var harrow = vm.Places.Single(p => p.Place == "Harrow, Middlesex, England");
        harrow.Coordinates = "51.5791, -0.3368";
        Assert.True(vm.HasPersonPins);                       // the open map updates straight away
    }

    [AvaloniaFact]
    public async Task PhotoEditsFromTheViewerAreUndoableAndTagsFollowMergesAndDeletes()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var me = vm.Tree.People[1];
        var photo = new MediaLink { File = "C:/nowhere/family.jpg", MediaType = "PHOTO", Title = "Family" };
        me.Media.Add(photo);
        vm.NavigateCommand.Execute(1L);
        vm.NavigateCommand.Execute(1L);

        dialogs.Photos = viewer =>
        {
            viewer.AddTag(6, new Rect(0.1, 0.1, 0.2, 0.3));    // tag "Thomas Whitfeld", the duplicate
            return true;
        };
        await vm.OpenPhotoCommand.ExecuteAsync(vm.Photos.Single());
        Assert.Single(photo.Regions);

        // Thomas Whitfeld's page shows the photo he's tagged in.
        vm.NavigateCommand.Execute(6L);
        Assert.Single(vm.Photos);

        // Merging him into Thomas moves the tag.
        dialogs.Duplicates = d => d.MergeCommand.Execute(null);
        await vm.FindDuplicatesCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.Tree.People[1].Media.Single().Regions.Single().PersonId);

        vm.UndoCommand.Execute(null);                         // undo the merge
        Assert.Equal(6, vm.Tree.People[1].Media.Single().Regions.Single().PersonId);
        vm.UndoCommand.Execute(null);                         // undo the tagging
        Assert.Empty(vm.Tree.People[1].Media.Single().Regions);
    }

    [AvaloniaFact]
    public async Task EventsCanHavePhotos()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Event = editor =>
        {
            Assert.True(editor.CanAddPhotos);
            return EditorResult.Saved;
        };
        await vm.EditEventCommand.ExecuteAsync(vm.Events.First());
        // Picking files goes through OpenFilesAsync (none in tests), so the list is unchanged but the path works.
        Assert.Empty(vm.Tree.People[1].Birth!.Media);
        Assert.Equal(Path.GetFullPath(Path.Combine("C:/trees", "Mine media", "b.jpg")),
                     Path.GetFullPath(MediaLibrary.Resolve("C:/trees/Mine.lineage", Path.Combine("Mine media", "b.jpg"))));
    }
}
