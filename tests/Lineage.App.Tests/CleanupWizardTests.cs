using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Lineage.App.ViewModels;
using Lineage.App.Views;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class CleanupWizardTests
{
    /// <summary>The demo tree with a surname in capitals and a date the program can't read (set outside undo, as an import would).</summary>
    private static async Task<(MainViewModel Vm, FakeDialogs Dialogs)> MessyAsync()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People.Values.First(p => p.PrimaryName.Given == "Thomas Edward");
        thomas.PrimaryName.Surname = "WHITFIELD";
        thomas.Events.Add(new Event { Tag = "OCCU", Value = "Clerk", Date = GenDate.Parse("03/04/1870") });
        return (vm, dialogs);
    }

    [AvaloniaFact]
    public async Task TickedChangesAreMadeAsOneUndoableStep()
    {
        var (vm, dialogs) = await MessyAsync();
        CleanupViewModel? seen = null;
        dialogs.Cleanup = w => { seen = w; return true; };
        await vm.CleanUpCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.Single(seen!.Names.Rows);
        var date = Assert.Single(seen.Dates.Rows);
        Assert.False(date.IsChosen);   // 03/04 could be read either way: left for the user

        var thomas = vm.Tree.People.Values.First(p => p.PrimaryName.Given == "Thomas Edward");
        Assert.Equal("Whitfield", thomas.PrimaryName.Surname);
        Assert.Equal(DateKind.Text, thomas.Events.Last().Date.Kind);
        Assert.Equal("clean up names, dates and places", vm.Tree.History[^1].Label);

        vm.UndoCommand.Execute(null);
        Assert.Equal("WHITFIELD", vm.Tree.Person(thomas.Id)!.PrimaryName.Surname);
    }

    [AvaloniaFact]
    public async Task TheDateOrderSettingRereadsDates()
    {
        var (vm, _) = await MessyAsync();
        var wizard = new CleanupViewModel(vm.Tree, dayFirst: true);
        Assert.Equal("3 Apr 1870", wizard.Dates.Rows.Single().After, ignoreCase: true);
        wizard.MonthFirst = true;
        Assert.Equal("4 Mar 1870", wizard.Dates.Rows.Single().After, ignoreCase: true);
    }

    [AvaloniaFact]
    public async Task CancellingChangesNothing()
    {
        var (vm, dialogs) = await MessyAsync();
        var before = vm.Tree.History.Count;
        await vm.CleanUpCommand.ExecuteAsync(null);   // the fake answers Cancel
        Assert.Equal(before, vm.Tree.History.Count);
    }

    /// <summary>Renders the new windows in both themes into the test output folder, to look at.</summary>
    [AvaloniaFact]
    public async Task WindowsRender()
    {
        var (vm, dialogs) = await MessyAsync();
        dialogs.Person = e => { e.Given = "Tommy"; return true; };
        await vm.EditPersonCommand.ExecuteAsync(null);
        HistoryViewModel? history = null;
        dialogs.History = h => history = h;
        await vm.ShowHistoryCommand.ExecuteAsync(null);
        var folder = Path.Combine(AppContext.BaseDirectory, "screens");
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Avalonia.Application.Current!.RequestedThemeVariant = theme;
            foreach (var (name, window) in new (string, Avalonia.Controls.Window)[]
                     {
                         ("cleanup", new CleanupWindow { DataContext = new CleanupViewModel(vm.Tree, dayFirst: true) }),
                         ("history", new HistoryWindow { DataContext = history }),
                     })
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame()!;
                frame.Save(Path.Combine(folder, $"{name}-{theme}.png"));
                window.Close();
            }
        }
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }
}
