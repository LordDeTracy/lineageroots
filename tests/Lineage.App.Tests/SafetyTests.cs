using Avalonia.Headless.XUnit;
using Lineage.App.Services;
using Lineage.App.ViewModels;
using Lineage.Core.Storage;

namespace Lineage.App.Tests;

/// <summary>Backups, recovery copies, the close question, bookmarks and the home person.</summary>
public class SafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lineage-safety-{Guid.NewGuid():N}");
    private readonly string _previousRoot = Backups.Root;

    public SafetyTests() => Backups.Root = _root;

    public void Dispose()
    {
        Backups.Root = _previousRoot;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task SavingKeepsThePreviousVersionAndTrimsOldOnes()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(_root, "Tree.lineage");
        Directory.CreateDirectory(_root);
        dialogs.SavePath = path;
        vm.Settings.BackupsToKeep = 2;
        for (var i = 0; i < 4; i++)
        {
            await vm.SaveAsCommand.ExecuteAsync(null);
            File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-10 + i)); // distinct backup names
        }
        var copies = Directory.GetFiles(Path.Combine(Backups.BackupFolder, "Tree"), "*.lineage");
        Assert.Equal(2, copies.Length);
        Assert.Equal(6, LineageStore.Load(copies[0]).People.Count);
    }

    [AvaloniaFact]
    public async Task RecoveryCopyIsOfferedAndRestored()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(_root, "Mine.lineage");
        Directory.CreateDirectory(_root);
        dialogs.SavePath = path;
        await vm.SaveAsCommand.ExecuteAsync(null);
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-5));

        // An unsaved edit, then a recovery copy (as the timer would write), then a "crash".
        dialogs.Person = e => { e.Given = "Tommy"; return true; };
        await vm.EditPersonCommand.ExecuteAsync(null);
        await vm.SaveRecoveryNowAsync();
        Assert.NotNull(Backups.For(path));

        var (fresh, freshDialogs) = (new MainViewModel(new FakeDialogs { ConfirmAnswer = true }), 0);
        _ = freshDialogs;
        await fresh.OpenPathAsync(path);
        Assert.Equal("Tommy Whitfield", fresh.CurrentName);
        Assert.True(fresh.IsDirty);
        Assert.Equal(path, fresh.FilePath);

        await fresh.SaveCommand.ExecuteAsync(null);
        Assert.Null(Backups.For(path));                     // saved, so nothing left to recover
    }

    [AvaloniaFact]
    public async Task ClosingAsksAboutUnsavedChanges()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        Assert.True(vm.IsDirty);                            // an import is unsaved until saved
        dialogs.SaveAnswer = SaveChoice.Cancel;
        Assert.False(await vm.CanCloseAsync());
        dialogs.SaveAnswer = SaveChoice.DontSave;
        Assert.True(await vm.CanCloseAsync());
        dialogs.SaveAnswer = SaveChoice.Save;
        dialogs.SavePath = null;                            // the user cancels the Save As dialog
        Assert.False(await vm.CanCloseAsync());
    }

    [AvaloniaFact]
    public async Task BookmarksAndHomePersonAreSavedWithTheTree()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.NavigateCommand.Execute(5L);
        vm.ToggleBookmarkCommand.Execute(null);
        vm.SetHomePersonCommand.Execute(null);
        Assert.True(vm.IsBookmarked);
        Assert.True(vm.IsHomePerson);
        Assert.Single(vm.BookmarkCards);

        var path = Path.Combine(_root, "Marks.lineage");
        Directory.CreateDirectory(_root);
        dialogs.SavePath = path;
        await vm.SaveAsCommand.ExecuteAsync(null);
        var back = new MainViewModel(new FakeDialogs());
        await back.OpenPathAsync(path);
        Assert.Equal("Clara Whitfield", back.CurrentName);  // opens on the home person
        Assert.True(back.IsBookmarked);

        back.ToggleBookmarkCommand.Execute(null);
        Assert.False(back.IsBookmarked);
        Assert.Empty(back.BookmarkCards);
    }
}
