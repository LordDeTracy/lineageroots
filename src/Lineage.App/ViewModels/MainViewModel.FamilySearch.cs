using CommunityToolkit.Mvvm.Input;
using Lineage.App.Services;
using Lineage.Core.FamilySearch;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AppSettings _settings = AppSettings.Load();
    private FamilySearchApi? _familySearch;

    private bool CanUseFamilySearchId() => _current is { FamilySearchId.Length: > 0 };

    /// <summary>Opens the person's page on FamilySearch.org — works without signing in.</summary>
    [RelayCommand(CanExecute = nameof(CanUseFamilySearchId))]
    private void OpenOnFamilySearch()
    {
        if (_current is { FamilySearchId.Length: > 0 } p) OpenExternal(FamilySearchApi.PersonPageUrl(p.FamilySearchId));
    }

    [RelayCommand]
    private async Task FamilySearchSettingsAsync()
    {
        var vm = new FamilySearchSettingsViewModel(_settings);
        if (!await _dialogs.FamilySearchSettingsAsync(vm)) return;
        vm.Apply(_settings);
        try { _settings.Save(); }
        catch (Exception ex) { await _dialogs.ShowMessageAsync("Could not save settings", ex.Message); }
        _familySearch = null; // the key or environment may have changed: sign in again
    }

    [RelayCommand]
    private async Task FamilySearchSignInAsync() => await EnsureFamilySearchAsync(forceSignIn: true);

    /// <summary>Makes sure there's an app key and a signed-in session; returns the client, or null if the user gave up.</summary>
    private async Task<FamilySearchApi?> EnsureFamilySearchAsync(bool forceSignIn = false)
    {
        if (_settings.FamilySearchAppKey.Length == 0)
        {
            await FamilySearchSettingsAsync();
            if (_settings.FamilySearchAppKey.Length == 0) return null;
        }
        _familySearch ??= new FamilySearchApi(_settings.FamilySearchIntegration);
        if (_familySearch.SignedIn && !forceSignIn) return _familySearch;
        try
        {
            Status = "Waiting for you to sign in to FamilySearch in your browser…";
            await FamilySearchSignIn.SignInAsync(_familySearch, _settings, CancellationToken.None);
            Status = $"Signed in to FamilySearch{(_settings.FamilySearchIntegration ? " (integration)" : "")}.";
            return _familySearch;
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("FamilySearch", ex.Message);
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task FamilySearchFindAsync()
    {
        if (_current is not { } p || await EnsureFamilySearchAsync() is not { } api) return;
        var found = await _dialogs.FamilySearchSearchAsync(new FamilySearchSearchViewModel(api, p));
        if (found == null) return;
        Change("link to FamilySearch", new[] { p }, () => p.FamilySearchId = found.Id);
        Status = $"Linked {p.DisplayName} to FamilySearch {found.Id}.";
        if (await _dialogs.ConfirmAsync("FamilySearch", $"Linked to {found.Id}. Compare the details now?"))
            await FamilySearchCompareAsync();
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task FamilySearchCompareAsync()
    {
        if (_current is not { } p) return;
        if (p.FamilySearchId.Length == 0) { await FamilySearchFindAsync(); return; }
        if (await EnsureFamilySearchAsync() is not { } api) return;
        try
        {
            Status = "Reading from FamilySearch…";
            var remote = await api.PersonAsync(p.FamilySearchId);
            Status = "";
            if (remote == null) { await _dialogs.ShowMessageAsync("FamilySearch", "FamilySearch returned no details for this person."); return; }
            var compare = new FamilySearchCompareViewModel(p, remote);
            if (!await _dialogs.FamilySearchCompareAsync(compare)) return;
            var keys = compare.ChosenKeys.ToList();
            if (keys.Count == 0) return;
            Change("copy from FamilySearch", new[] { p }, () => FsSync.Apply(p, remote, keys));
            Status = $"Copied {keys.Count} detail{(keys.Count == 1 ? "" : "s")} from FamilySearch.";
        }
        catch (Exception ex) { Status = ""; await _dialogs.ShowMessageAsync("FamilySearch", ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task FamilySearchDownloadAncestorsAsync()
    {
        if (_current is not { } p) return;
        if (p.FamilySearchId.Length == 0)
        {
            await _dialogs.ShowMessageAsync("FamilySearch", "Link this person to FamilySearch first (Find on FamilySearch), then download their ancestors.");
            return;
        }
        if (!await _dialogs.ConfirmAsync("Download ancestors",
                $"Download up to 4 generations of {p.DisplayName}'s ancestors from FamilySearch? People already in your tree are linked, not duplicated. Ctrl+Z undoes it.")) return;
        if (await EnsureFamilySearchAsync() is not { } api) return;
        try
        {
            Status = "Downloading ancestors from FamilySearch…";
            var ancestry = await api.AncestryAsync(p.FamilySearchId, 4);
            // Everyone the download can touch: the local ancestors and anyone already carrying one of these IDs.
            var ids = ancestry.Select(a => a.Id).ToHashSet();
            var touched = _tree.Ancestors(p, 6).Values.SelectMany(_tree.Around)
                .Concat(_tree.People.Values.Where(x => ids.Contains(x.FamilySearchId)).SelectMany(_tree.Around)).Distinct().ToList();
            var added = 0;
            Change("download ancestors", touched, () => added = FsSync.AddAncestors(_tree, p, ancestry));
            Status = added == 0 ? "Your tree already has these ancestors; links to FamilySearch were updated."
                                : $"Added {added} ancestor{(added == 1 ? "" : "s")} from FamilySearch. Ctrl+Z undoes it.";
            if (added > 0) View = PersonView.Pedigree;
        }
        catch (Exception ex) { Status = ""; await _dialogs.ShowMessageAsync("FamilySearch", ex.Message); }
    }
}
