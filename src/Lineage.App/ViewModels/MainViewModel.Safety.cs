using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.App.Services;
using Lineage.Core.Storage;

namespace Lineage.App.ViewModels;

public enum SaveChoice { Save, DontSave, Cancel }

/// <summary>Auto-save to a recovery copy, recovery after a crash, and the "save before closing?" question.</summary>
public sealed partial class MainViewModel
{
    private DispatcherTimer? _autoSaveTimer;
    private DateTime _lastRecoverySave = DateTime.Now;
    private bool _autoSaving;

    /// <summary>Unsaved work found from an earlier session, offered on the welcome screen.</summary>
    public ObservableCollection<RecoveryEntry> Recoverable { get; } = new();
    [ObservableProperty] private bool _hasRecoverable;

    /// <summary>Starts the auto-save clock. Called once the window is up (not in tests).</summary>
    public void StartAutoSave()
    {
        RefreshRecoverable();
        _autoSaveTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, async (_, _) => await AutoSaveTickAsync());
        _autoSaveTimer.Start();
    }

    private async Task AutoSaveTickAsync()
    {
        var minutes = _settings.AutoSaveMinutes;
        if (_autoSaving || minutes <= 0 || !HasTree || !IsDirty || DateTime.Now - _lastRecoverySave < TimeSpan.FromMinutes(minutes)) return;
        await SaveRecoveryNowAsync();
    }

    /// <summary>Writes a recovery copy now. Edits only happen on the UI thread, so a snapshot is taken first.</summary>
    internal async Task SaveRecoveryNowAsync()
    {
        _autoSaving = true;
        try
        {
            // Copy every record on the UI thread (fast), then write the copy in the background.
            var snapshot = new Lineage.Core.Model.FamilyTree { Name = _tree.Name, HomePersonId = _tree.HomePersonId };
            foreach (var p in _tree.People.Values) snapshot.AddPerson(LineageStore.FromJson<Lineage.Core.Model.Person>(LineageStore.ToJson(p)));
            foreach (var f in _tree.Families.Values) snapshot.AddFamily(LineageStore.FromJson<Lineage.Core.Model.Family>(LineageStore.ToJson(f)));
            foreach (var s in _tree.Sources.Values) snapshot.AddSource(LineageStore.FromJson<Lineage.Core.Model.Source>(LineageStore.ToJson(s)));
            foreach (var r in _tree.Repositories.Values) snapshot.AddRepository(LineageStore.FromJson<Lineage.Core.Model.Repository>(LineageStore.ToJson(r)));
            foreach (var r in _tree.Research.Values) snapshot.AddResearch(LineageStore.FromJson<Lineage.Core.Model.ResearchItem>(LineageStore.ToJson(r)));
            snapshot.Bookmarks.AddRange(_tree.Bookmarks);
            snapshot.NotDuplicates.UnionWith(_tree.NotDuplicates);
            var path = FilePath;
            await Task.Run(() => Backups.SaveRecovery(snapshot, path));
            _lastRecoverySave = DateTime.Now;
        }
        catch (Exception) { /* auto-save is best effort; the next tick tries again */ }
        finally { _autoSaving = false; }
    }

    internal void RefreshRecoverable()
    {
        Recoverable.Clear();
        try
        {
            foreach (var e in Backups.Pending())
                if (!(HasTree && string.Equals(e.OriginalPath, FilePath, StringComparison.OrdinalIgnoreCase) && e.TreeName == _tree.Name))
                    Recoverable.Add(e);
        }
        catch (Exception) { }
        HasRecoverable = Recoverable.Count > 0;
    }

    private async Task OfferRecoveryAsync(RecoveryEntry pending)
    {
        if (await _dialogs.ConfirmAsync("Recover unsaved changes?",
                $"Lineage Roots found changes to this tree from {pending.SavedAt:g} that were never saved — the app may have closed unexpectedly. Restore them?"))
            await RecoverAsync(pending);
        else Backups.Discard(pending);
    }

    /// <summary>Opens a recovery copy, keeping the original file name so Save writes back to it.</summary>
    [RelayCommand]
    private async Task RecoverAsync(RecoveryEntry entry)
    {
        try
        {
            var tree = await Task.Run(() => LineageStore.Load(entry.RecoveryFile));
            tree.Name = entry.TreeName;
            Load(tree, entry.OriginalPath, "Recovered", 0);
            IsDirty = true;
            Status = $"Recovered unsaved changes from {entry.SavedAt:g}. Save to keep them.";
        }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Could not recover", ex.Message); }
        RefreshRecoverable();
    }

    [RelayCommand]
    private void DiscardRecovery(RecoveryEntry entry)
    {
        Backups.Discard(entry);
        RefreshRecoverable();
    }

    internal AppSettings Settings => _settings;

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var vm = new SettingsViewModel(_settings);
        if (!await _dialogs.EditSettingsAsync(vm)) return;
        vm.Apply(_settings);
        try { _settings.Save(); }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Could not save settings", ex.Message); }
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = _settings.Theme switch
            {
                "Dark" => Avalonia.Styling.ThemeVariant.Dark,
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
        Lineage.App.Controls.MapView.AllowOnlineTiles = _settings.OnlineMaps;
        OnPropertyChanged(nameof(OnlineMaps));
    }

    /// <summary>Whether map views may download street maps (a setting).</summary>
    public bool OnlineMaps => _settings.OnlineMaps;

    public void RememberTheme(string theme)
    {
        _settings.Theme = theme;
        try { _settings.Save(); } catch (Exception) { /* not worth interrupting for */ }
    }

    [RelayCommand]
    private static void OpenBackupsFolder()
    {
        Directory.CreateDirectory(Backups.BackupFolder);
        OpenExternal(Backups.BackupFolder);
    }

    /// <summary>Asks to save unsaved changes before the window closes; false means stay open.</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (!HasTree || !IsDirty || _tree.People.Count == 0)
        {
            if (HasTree) Backups.ClearRecovery(FilePath, _tree.Name);
            return true;
        }
        switch (await _dialogs.AskSaveChangesAsync(TreeName))
        {
            case SaveChoice.Save:
                await SaveAsync();
                return !IsDirty;
            case SaveChoice.DontSave:
                Backups.ClearRecovery(FilePath, _tree.Name);
                return true;
            default:
                return false;
        }
    }
}
