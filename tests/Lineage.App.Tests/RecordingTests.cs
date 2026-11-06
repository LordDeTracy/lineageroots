using Avalonia.Headless.XUnit;
using Lineage.Core.Media;

namespace Lineage.App.Tests;

public class RecordingTests
{
    [AvaloniaFact]
    public async Task SoundVideoAndDocumentsSitBesideThePhotosAndPlayElsewhere()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        vm.NavigateCommand.Execute(thomas.Id);
        var folder = Directory.CreateTempSubdirectory("lineage-media-").FullName;
        try
        {
            foreach (var name in new[] { "interview.mp3", "wedding.mp4", "will.pdf" })
            {
                var path = Path.Combine(folder, name);
                await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
                dialogs.OpenPaths.Add(path);
            }
            await vm.AddPhotosCommand.ExecuteAsync(null);
            Assert.Equal("Added 3 recordings or documents", vm.Status);
            Assert.DoesNotContain(thomas.Media, m => m.Primary);                         // a recording can't be the profile photo
            Assert.Equal(new[] { "AUDIO", "VIDEO", "DOCUMENT" }, thomas.Media.Select(m => m.MediaType));

            var tiles = vm.Photos.ToList();
            Assert.Equal(new[] { MediaKind.Sound, MediaKind.Video, MediaKind.Document }, tiles.Select(t => t.Kind));
            Assert.Equal("Sound recording", tiles[0].Caption);
            Assert.True(tiles[0].IsPlayable && !tiles[2].IsPlayable && tiles[2].IsDocument);

            var viewers = new List<object>();
            dialogs.Photos = v => { viewers.Add(v); return false; };
            TestApp.Opened.Clear();
            await vm.OpenPhotoCommand.ExecuteAsync(tiles[0]);                              // plays in the computer's own player
            Assert.Equal(Path.Combine(folder, "interview.mp3"), Assert.Single(TestApp.Opened));
            Assert.Empty(viewers);
        }
        finally { Directory.Delete(folder, true); }
    }
}
