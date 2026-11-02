using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lineage.App.ViewModels;
using Lineage.Core.Sharing;

namespace Lineage.App.Tests;

public class LocalShareViewTests
{
    [AvaloniaFact]
    public async Task ATreeIsSharedWithALaptopThatHasNoneOpen()
    {
        var (hostVm, hostDialogs) = await Demo.OpenAsync();
        hostVm.ShareOnNetworkCommand.Execute(null);
        var hosting = hostDialogs.LastLocalShare!;
        hosting.StartSharingCommand.Execute(null);
        Assert.True(hosting.IsHosting);
        Assert.Equal(4, hosting.Pin.Length);

        // Someone arriving with no tree open (their copy of the program, on the same network).
        var joiner = new MainViewModel(new FakeDialogs());
        using var joining = joiner.NewLocalShare(() => Random.Shared.Next(40000, 49000));
        var port = int.Parse(hosting.PhoneAddress.Split(':').Last().Split(' ')[0]);
        joining.SelectedTree = new SharedTree("t", "Demo", "HOST-PC", "127.0.0.1", port);
        joining.YourName = "Ann";

        joining.JoinPin = "0000";
        await joining.GetTreeCommand.ExecuteAsync(null);
        Assert.Equal("That PIN isn't right.", joining.JoinStatus);

        joining.JoinPin = hosting.Pin;
        await joining.GetTreeCommand.ExecuteAsync(null);
        Assert.StartsWith("The host has been asked", joining.JoinStatus);
        Dispatcher.UIThread.RunJobs();
        var ann = Assert.Single(hosting.Devices);
        Assert.Equal(("Ann", true), (ann.Name, ann.IsWaiting));

        hosting.AllowCommand.Execute(ann);
        await joining.GetTreeCommand.ExecuteAsync(null);
        Assert.True(joiner.HasTree);   // opened as a tree of its own
        Assert.Contains(joiner.Tree.People.Values, p => p.DisplayName == "Thomas Edward Whitfield");

        hosting.StopSharingCommand.Execute(null);
        hosting.Dispose();
    }
}
