using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class TutorialTests
{
    [AvaloniaFact]
    public async Task TheTourShowsAtStartUntilTurnedOff()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(dialogs);
        var shown = 0;
        dialogs.Tutorial = tour =>
        {
            shown++;
            Assert.Equal("Welcome to Lineage Roots", tour.Page.Title);
            Assert.False(tour.DontShowAgain);
            while (!tour.IsLast) tour.NextCommand.Execute(null);
            Assert.True(tour.IsShortcuts);
            Assert.Contains(TutorialViewModel.Shortcuts, s => s.Keys == "Ctrl+K");
            tour.DontShowAgain = true;
        };
        await vm.ShowTutorialAtStartAsync();
        Assert.Equal(1, shown);
        Assert.False(vm.Settings.ShowTutorial);

        await vm.ShowTutorialAtStartAsync();   // turned off: not again at start
        Assert.Equal(1, shown);
        dialogs.Tutorial = tour => Assert.True(tour.DontShowAgain);
        await vm.ShowTutorialCommand.ExecuteAsync(null);   // F1 still opens it
    }

    [AvaloniaFact]
    public async Task AltNumbersJumpBetweenSections()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.ShowSectionCommand.Execute(AppSection.Reports);
        Assert.True(vm.IsReportsSection);
        vm.ShowViewCommand.Execute(PersonView.Statistics);   // Ctrl+0
        Assert.Equal(AppSection.Charts, vm.Section);
    }
}
