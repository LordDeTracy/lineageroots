using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class SideBySideTests
{
    [AvaloniaFact]
    public async Task TwoPeopleSideBySideAndOpenEither()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        var george = vm.Tree.People.Values.Single(p => p.DisplayName == "George Whitfield");
        vm.NavigateCommand.Execute(thomas.Id);
        dialogs.SideBySide = view =>
        {
            Assert.Equal("Thomas Edward Whitfield", view.Left.Name);
            Assert.Contains(view.Left.Lines, l => l.Label == "Birth" && l.Text.Contains("Harrow"));
            Assert.Contains(view.Left.Lines, l => l.Label == "Father" && l.Text.StartsWith("George Whitfield"));
            view.RightName = EventEditorViewModel.PersonLabel(george);
            Assert.Equal("George Whitfield", view.Right.Name);
            view.SwapCommand.Execute(null);
            Assert.Equal("George Whitfield", view.Left.Name);
            view.OpenCommand.Execute(view.Left);
            return Task.CompletedTask;
        };
        await vm.SideBySideCommand.ExecuteAsync(null);
        Assert.Equal("George Whitfield", vm.CurrentName);
    }
}
