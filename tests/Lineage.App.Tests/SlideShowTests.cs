using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class SlideShowTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [AvaloniaFact]
    public async Task PhotosPlayOneAfterAnotherWithWhoIsInThem()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        var folder = Directory.CreateTempSubdirectory("lineage-slides-").FullName;
        try
        {
            foreach (var name in new[] { "wedding.png", "garden.png" })
                await File.WriteAllBytesAsync(Path.Combine(folder, name), Png);
            var wedding = new MediaLink { File = Path.Combine(folder, "wedding.png"), Title = "Wedding day", Taken = "18 JUN 1868" };
            wedding.Regions.Add(new PhotoRegion { PersonId = thomas.Id });
            thomas.Media.Add(wedding);
            thomas.Media.Add(new MediaLink { File = Path.Combine(folder, "garden.png") });
            thomas.Media.Add(new MediaLink { File = Path.Combine(folder, "gone.png") });              // missing: left out
            thomas.Media.Add(new MediaLink { File = Path.Combine(folder, "talk.mp3") });              // not a photo
            vm.NavigateCommand.Execute(thomas.Id);

            await vm.SlideShowCommand.ExecuteAsync(null);
            var show = Assert.Single(dialogs.SlideShows);
            Assert.Equal(2, show.Slides.Count);
            Assert.Equal("Wedding day", show.Title);
            Assert.Contains("Thomas Edward Whitfield", show.Detail);
            Assert.Equal("1 of 2", show.Position);
            Assert.NotNull(show.Picture);
            show.Next();
            Assert.Equal("garden", show.Title);
            show.Next();
            Assert.Equal("1 of 2", show.Position);                                                  // wraps round
            show.Previous();
            Assert.Equal("2 of 2", show.Position);
            show.Start();
            Assert.True(show.Playing);
            show.TogglePlayCommand.Execute(null);
            Assert.False(show.Playing);

            await vm.SlideShowAllCommand.ExecuteAsync(null);
            Assert.Equal(2, dialogs.SlideShows.Count);
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public async Task NoPhotosSaysSo()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        await vm.SlideShowAllCommand.ExecuteAsync(null);
        Assert.Empty(dialogs.SlideShows);
        Assert.Contains(dialogs.Messages, m => m.Contains("no photos"));
    }
}
