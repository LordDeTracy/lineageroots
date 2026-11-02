using CommunityToolkit.Mvvm.Input;
using Lineage.App.Services;
using Lineage.Core.Editing;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.App.ViewModels;

/// <summary>A password on the tree file, and the change history saved in it.</summary>
public sealed partial class MainViewModel
{
    // ---- password ----

    /// <summary>The open tree is saved with a password.</summary>
    public bool IsLocked => HasTree && _tree.Lock != null;
    public string PasswordMenuText => IsLocked ? "Change password…" : "Protect with a password…";

    private void NotifyLock()
    {
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(PasswordMenuText));
        RemovePasswordCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reads a .lineage file, asking for its password when it has one (again after a wrong one).
    /// Null when the user cancelled.
    /// </summary>
    internal async Task<FamilyTree?> LoadLineageAsync(string path)
    {
        if (!FileLock.IsLocked(path)) return await Task.Run(() => LineageStore.Load(path));
        var hint = FileLock.HintOf(path);
        var wrong = false;
        while (true)
        {
            var password = await _dialogs.AskPasswordAsync(Path.GetFileNameWithoutExtension(path), hint, wrong);
            if (password == null) return null;
            try { return await Task.Run(() => LineageStore.Load(path, password)); }
            catch (WrongPasswordException) { wrong = true; }
        }
    }

    [RelayCommand]
    private async Task SetPasswordAsync()
    {
        if (!HasTree) return;
        var changing = IsLocked;
        if (await _dialogs.ChoosePasswordAsync(changing) is not { } choice) return;
        Status = "Setting the password…";
        _tree.Lock = await Task.Run(() => FileLock.Create(choice.Password, choice.Hint));
        NotifyLock();
        if (FilePath == null) await SaveAsAsync();
        else await SaveToAsync(FilePath);
        if (IsDirty) { Status = "The password is set; save the tree to apply it."; return; }
        Status = changing ? "Password changed." : "This tree is now protected with a password.";
        await OfferToDeleteOldBackupsAsync();
    }

    private bool CanRemovePassword() => IsLocked;

    [RelayCommand(CanExecute = nameof(CanRemovePassword))]
    private async Task RemovePasswordAsync()
    {
        if (!await _dialogs.ConfirmAsync("Remove the password?",
                "The tree will be saved without a password, so anyone who can get to the file can open it. Remove the password?")) return;
        _tree.Lock = null;
        NotifyLock();
        if (FilePath != null) await SaveToAsync(FilePath);
        else MarkChanged();
        Status = "The password is removed.";
    }

    /// <summary>Backups saved before the password was set are not protected; offers to delete them.</summary>
    private async Task OfferToDeleteOldBackupsAsync()
    {
        if (FilePath is not { } path) return;
        var open = Backups.UnlockedBackupsOf(path);
        if (open.Count == 0) return;
        if (!await _dialogs.ConfirmAsync("Delete unprotected backups?",
                $"There {(open.Count == 1 ? "is 1 backup" : $"are {open.Count} backups")} of this tree from before it had a password, " +
                "and anyone could open them. Delete them? (New backups will have the password.)")) return;
        var deleted = open.Count(f => { try { File.Delete(f); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } });
        Status = $"Deleted {deleted} unprotected backup{(deleted == 1 ? "" : "s")}.";
    }

    // ---- change history ----

    /// <summary>Who the change history names: the researcher's name from Settings, or the Windows user.</summary>
    private void SetHistoryAuthor() =>
        _history.Author = () => _settings.ResearcherName is { Length: > 0 } name ? name.Trim() : Environment.UserName;

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        if (!HasTree) return;
        var view = new HistoryViewModel(_tree.History, change =>
        {
            var count = _history.UndoFrom(change);
            if (count > 0) AfterHistoryMove($"Undid {count} change{(count == 1 ? "" : "s")}");
            return count;
        }, async () =>
        {
            if (!await _dialogs.ConfirmAsync("Clear the history?",
                    "Forget every change listed here? They can no longer be undone, and the list is removed from the file when it's saved. Your tree itself doesn't change.")) return false;
            _history.Clear();
            NotifyUndo();
            MarkChanged();
            return true;
        });
        await _dialogs.ShowHistoryAsync(view);
    }
}
