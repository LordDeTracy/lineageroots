using Avalonia.Headless.XUnit;
using Lineage.App.Services;
using Lineage.App.ViewModels;
using Lineage.App.Views;
using Lineage.Core.Storage;

namespace Lineage.App.Tests;

/// <summary>Protecting a tree with a password, opening it again, and the change history window.</summary>
public class ProtectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lineage-protect-{Guid.NewGuid():N}");
    private readonly string _previousRoot = Backups.Root;

    public ProtectionTests()
    {
        Backups.Root = _root;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Backups.Root = _previousRoot;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<(MainViewModel Vm, FakeDialogs Dialogs, string Path)> SavedDemoAsync()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(_root, "Family.lineage");
        dialogs.SavePath = path;
        await vm.SaveAsCommand.ExecuteAsync(null);
        return (vm, dialogs, path);
    }

    [AvaloniaFact]
    public async Task SettingAPasswordEncryptsTheFileAndOffersToDeleteOldBackups()
    {
        var (vm, dialogs, path) = await SavedDemoAsync();
        await vm.SaveCommand.ExecuteAsync(null);   // makes an unprotected backup
        Assert.NotEmpty(Backups.UnlockedBackupsOf(path));

        dialogs.NewPassword = new PasswordChoice("Harrow 1842", "parish and year");
        dialogs.ConfirmAnswer = true;
        await vm.SetPasswordCommand.ExecuteAsync(null);

        Assert.True(vm.IsLocked);
        Assert.Equal("Change password…", vm.PasswordMenuText);
        Assert.True(FileLock.IsLocked(path));
        Assert.Equal("parish and year", FileLock.HintOf(path));
        Assert.Empty(Backups.UnlockedBackupsOf(path));
    }

    [AvaloniaFact]
    public async Task OpeningALockedFileAsksUntilThePasswordIsRight()
    {
        var (vm, dialogs, path) = await SavedDemoAsync();
        dialogs.NewPassword = new PasswordChoice("Harrow 1842", "parish and year");
        await vm.SetPasswordCommand.ExecuteAsync(null);

        var other = new FakeDialogs();
        other.Passwords.Enqueue("harrow 1842");
        other.Passwords.Enqueue("Harrow 1842");
        var fresh = new MainViewModel(other);
        await fresh.OpenPathAsync(path);

        Assert.True(fresh.HasTree);
        Assert.True(fresh.IsLocked);
        Assert.Equal(6, fresh.Tree.People.Count);
        Assert.Equal(new[] { ("parish and year", false), ("parish and year", true) }, other.PasswordAsks);
        Assert.Empty(other.Messages);
    }

    [AvaloniaFact]
    public async Task CancellingThePasswordOpensNothing()
    {
        var (vm, dialogs, path) = await SavedDemoAsync();
        dialogs.NewPassword = new PasswordChoice("pw", "");
        await vm.SetPasswordCommand.ExecuteAsync(null);

        var other = new FakeDialogs();   // no answers queued: Cancel
        var fresh = new MainViewModel(other);
        await fresh.OpenPathAsync(path);
        Assert.False(fresh.HasTree);
        Assert.Empty(other.Messages);
    }

    [AvaloniaFact]
    public async Task RemovingThePasswordSavesAnOrdinaryFile()
    {
        var (vm, dialogs, path) = await SavedDemoAsync();
        dialogs.NewPassword = new PasswordChoice("pw", "");
        await vm.SetPasswordCommand.ExecuteAsync(null);
        dialogs.ConfirmAnswer = true;
        await vm.RemovePasswordCommand.ExecuteAsync(null);
        Assert.False(vm.IsLocked);
        Assert.False(FileLock.IsLocked(path));
    }

    [AvaloniaFact]
    public async Task HistoryIsKeptAcrossSessionsAndCanBeUndoneFromAnyPoint()
    {
        var (vm, dialogs, path) = await SavedDemoAsync();
        vm.Settings.ResearcherName = "Ann Researcher";
        dialogs.Person = e => { e.Given = "Tommy"; return true; };
        await vm.EditPersonCommand.ExecuteAsync(null);
        dialogs.Person = e => { e.Given = "Tom"; return true; };
        await vm.EditPersonCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);

        var other = new FakeDialogs();
        var fresh = new MainViewModel(other);
        await fresh.OpenPathAsync(path);
        Assert.Equal("Tom Whitfield", fresh.CurrentName);
        Assert.True(fresh.UndoCommand.CanExecute(null));   // undo works after reopening

        HistoryViewModel? shown = null;
        other.History = h => shown = h;
        await fresh.ShowHistoryCommand.ExecuteAsync(null);
        Assert.NotNull(shown);
        Assert.Equal(2, shown!.Rows.Count);
        Assert.Equal("Ann Researcher", shown.Rows[0].Who);
        Assert.Contains("Tom Whitfield", shown.Rows[0].Records);   // newest first

        shown.UndoFromCommand.Execute(shown.Rows[1]);   // the first edit and everything after it
        Assert.Equal("Thomas Edward Whitfield", fresh.CurrentName);
        Assert.Empty(shown.Rows);
        Assert.True(fresh.IsDirty);
    }

    [AvaloniaFact]
    public async Task ClearingTheHistoryForgetsIt()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Person = e => { e.Given = "Tommy"; return true; };
        await vm.EditPersonCommand.ExecuteAsync(null);
        HistoryViewModel? shown = null;
        dialogs.History = h => shown = h;
        await vm.ShowHistoryCommand.ExecuteAsync(null);
        Assert.Single(shown!.Rows);
        dialogs.ConfirmAnswer = true;
        await shown.ClearCommand.ExecuteAsync(null);
        Assert.Empty(shown.Rows);
        Assert.Empty(vm.Tree.History);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }
}
