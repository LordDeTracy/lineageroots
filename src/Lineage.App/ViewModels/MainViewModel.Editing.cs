using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed partial class MainViewModel
{
    // ---- undo ---------------------------------------------------------------------------------

    public string UndoText => _history.UndoLabel is { } l ? $"Undo {l}" : "Undo";
    public string RedoText => _history.RedoLabel is { } l ? $"Redo {l}" : "Redo";

    private void NotifyUndo()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(RedoText));
    }

    private bool CanUndo() => _history.CanUndo;
    private bool CanRedo() => _history.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_history.Undo() is { } label) AfterHistoryMove($"Undid: {label}");
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_history.Redo() is { } label) AfterHistoryMove($"Redid: {label}");
    }

    private void AfterHistoryMove(string message)
    {
        // Undo replaces record objects, so everything is looked up again by id.
        IsDirty = true;
        RebuildRows();
        RebuildSources();
        RebuildResearch();
        var id = _current?.Id ?? 0;
        if (_tree.Person(id) != null) Show(id, recordHistory: false);
        else if (_tree.People.Count > 0) Show(_tree.People.Keys.Min(), recordHistory: false);
        else ClearCurrent();
        NotifyUndo();
        Status = message;
    }

    /// <summary>
    /// Runs one edit with undo: <paramref name="touched"/> are the records it may change (new ones
    /// are found automatically). Afterwards the page shows <paramref name="showId"/> (default: the current person).
    /// </summary>
    private void Change(string label, IEnumerable<Entity> touched, Action mutate, long? showId = null)
    {
        var edit = _history.Begin(label, touched);
        try { mutate(); }
        catch { edit.Commit(); throw; } // keep whatever happened undoable
        edit.Commit();
        AfterChange(showId);
    }

    private void AfterChange(long? showId = null)
    {
        IsDirty = true;
        RebuildRows();
        RebuildSources();
        RebuildResearch();
        if (IsHomeSection) RebuildHome();
        if (IsPlacesSection) RebuildPlaceList();
        if (IsDnaSection) RebuildDna();
        var id = showId ?? _current?.Id;
        if (id is { } i && _tree.Person(i) != null) Show(i, recordHistory: showId != null && showId != _current?.Id);
        else if (_tree.People.Count > 0) Show(_tree.People.Keys.Min(), recordHistory: false);
        else ClearCurrent();
        NotifyUndo();
        Status = $"{_tree.People.Count:N0} people, {_tree.Families.Count:N0} families — unsaved changes";
    }

    private void NotifyPersonCommands()
    {
        EditPersonCommand.NotifyCanExecuteChanged();
        DeletePersonCommand.NotifyCanExecuteChanged();
        AddSpouseCommand.NotifyCanExecuteChanged();
        AddChildCommand.NotifyCanExecuteChanged();
        AddParentsCommand.NotifyCanExecuteChanged();
        AddPhotosCommand.NotifyCanExecuteChanged();
        AddEventCommand.NotifyCanExecuteChanged();
        AddResearchForPersonCommand.NotifyCanExecuteChanged();
        ShowDnaMatchesCommand.NotifyCanExecuteChanged();
        ToggleBookmarkCommand.NotifyCanExecuteChanged();
        SetHomePersonCommand.NotifyCanExecuteChanged();
        FamilyGroupReportCommand.NotifyCanExecuteChanged();
        AncestorReportCommand.NotifyCanExecuteChanged();
        DescendantReportCommand.NotifyCanExecuteChanged();
        RegisterReportCommand.NotifyCanExecuteChanged();
        IndividualSummaryCommand.NotifyCanExecuteChanged();
        FamilyBookCommand.NotifyCanExecuteChanged();
        OpenOnFamilySearchCommand.NotifyCanExecuteChanged();
        FamilySearchFindCommand.NotifyCanExecuteChanged();
        FamilySearchCompareCommand.NotifyCanExecuteChanged();
        FamilySearchDownloadAncestorsCommand.NotifyCanExecuteChanged();
    }

    // ---- people -------------------------------------------------------------------------------

    private bool HasCurrentPerson() => _current != null;

    private Task<bool> EditAsync(PersonEditorViewModel editor) => _dialogs.EditPersonAsync(editor);

    [RelayCommand]
    private async Task AddPersonAsync()
    {
        var p = new Person();
        var editor = new PersonEditorViewModel(p, "Add person");
        if (!await EditAsync(editor)) return;
        Change("add person", Array.Empty<Entity>(), () => { editor.Apply(); _tree.AddPerson(p); });
        Show(p.Id, recordHistory: true);
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task EditPersonAsync()
    {
        if (_current is not { } p) return;
        var editor = new PersonEditorViewModel(p, "Edit person", _tree);
        // The editor can also change the link to the parents (relationship, LDS sealing), so snapshot the family too.
        if (await EditAsync(editor)) Change($"edit {p.DisplayName}", _tree.Around(p), editor.Apply);
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task AddSpouseAsync()
    {
        if (_current is not { } p) return;
        var spouse = new Person { Sex = p.Sex == Sex.Male ? Sex.Female : p.Sex == Sex.Female ? Sex.Male : Sex.Unknown };
        var editor = new PersonEditorViewModel(spouse, $"Add spouse of {p.DisplayName}");
        if (!await EditAsync(editor)) return;
        Change("add spouse", _tree.Around(p), () =>
        {
            editor.Apply();
            _tree.AddPerson(spouse);
            var f = _tree.AddFamily(new Family { Status = CoupleStatus.Married });
            var (first, second) = p.Sex == Sex.Female ? (spouse, p) : (p, spouse);
            _tree.SetPartner(f, first, second: false);
            _tree.SetPartner(f, second, second: true);
        });
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task AddChildAsync()
    {
        if (_current is not { } p) return;
        var child = new Person();
        child.PrimaryName.Surname = p.Sex == Sex.Female && p.SpouseFamilyIds.Count > 0
            ? _tree.SpouseIn(_tree.Family(p.SpouseFamilyIds[0])!, p)?.PrimaryName.Surname ?? p.PrimaryName.Surname
            : p.PrimaryName.Surname;
        var editor = new PersonEditorViewModel(child, $"Add child of {p.DisplayName}");
        if (!await EditAsync(editor)) return;
        Change("add child", _tree.Around(p), () =>
        {
            editor.Apply();
            _tree.AddPerson(child);
            var f = _tree.Family(p.SpouseFamilyIds.FirstOrDefault());
            if (f == null)
            {
                f = _tree.AddFamily(new Family());
                _tree.SetPartner(f, p, second: p.Sex == Sex.Female);
            }
            _tree.AddChild(f, child);
        });
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task AddParentsAsync()
    {
        if (_current is not { } p) return;
        var father = new Person { Sex = Sex.Male };
        father.PrimaryName.Surname = p.PrimaryName.Surname;
        var fatherEditor = new PersonEditorViewModel(father, $"Add father of {p.DisplayName}");
        if (!await EditAsync(fatherEditor)) return;
        var mother = new Person { Sex = Sex.Female };
        var motherEditor = new PersonEditorViewModel(mother, $"Add mother of {p.DisplayName} (Cancel to skip)");
        var hasMother = await EditAsync(motherEditor);
        Change("add parents", _tree.Around(p), () =>
        {
            fatherEditor.Apply();
            _tree.AddPerson(father);
            var f = _tree.AddFamily(new Family { Status = CoupleStatus.Married });
            _tree.SetPartner(f, father, second: false);
            if (hasMother)
            {
                motherEditor.Apply();
                _tree.AddPerson(mother);
                _tree.SetPartner(f, mother, second: true);
            }
            _tree.AddChild(f, p);
        });
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task DeletePersonAsync()
    {
        if (_current is not { } p) return;
        if (!await _dialogs.ConfirmAsync("Delete person", $"Delete {p.DisplayName} and all their links? You can undo this with Ctrl+Z.")) return;
        var next = _back.Count > 0 ? _back.Pop() : (long?)null;
        // Photos of other people that this person is tagged in lose the tag too.
        var tagged = Lineage.Core.Media.MediaLibrary.TaggedIn(_tree, p.Id).Select(r => r.Owner);
        Change($"delete {p.DisplayName}", _tree.Around(p).Concat(tagged), () =>
        {
            _tree.RemovePerson(p);
            Lineage.Core.Media.MediaLibrary.Retag(_tree, p.Id, null);
        }, next);
    }

    // ---- events -------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task AddEventAsync()
    {
        if (_current is not { } p) return;
        var e = Event.Of(p.Find("BIRT") == null ? "BIRT" : "RESI");
        var editor = new EventEditorViewModel(_tree, e, isNew: true, forFamily: false, $"Add an event for {p.DisplayName}");
        editor.AttachPhotos(e, ResolveMedia, () => PickPhotosAsync("Add photos of this event"));
        editor.AttachOwner(p);
        if (await _dialogs.EditEventAsync(editor) != EditorResult.Saved) return;
        Change($"add {editor.Type.Label.ToLowerInvariant()}", editor.TouchedSources.Prepend<Entity>(p), () => { editor.Apply(e); p.Events.Add(e); });
    }

    [RelayCommand]
    private async Task EditEventAsync(EventRow row)
    {
        var forFamily = row.Owner is Family;
        var editor = new EventEditorViewModel(_tree, row.Event, isNew: false, forFamily, $"Edit {EventTypes.Label(row.Event).ToLowerInvariant()}");
        editor.AttachPhotos(row.Event, ResolveMedia, () => PickPhotosAsync("Add photos of this event"));
        editor.AttachOwner(row.Owner);
        var result = await _dialogs.EditEventAsync(editor);
        var events = row.Owner switch { Person p => p.Events, Family f => f.Events, _ => null };
        if (events == null) return;
        if (result == EditorResult.Saved)
            Change($"edit {EventTypes.Label(row.Event).ToLowerInvariant()}", editor.TouchedSources.Prepend(row.Owner), () => editor.Apply(row.Event));
        else if (result == EditorResult.Deleted)
            Change($"delete {EventTypes.Label(row.Event).ToLowerInvariant()}", new[] { row.Owner }, () => events.Remove(row.Event));
    }

    /// <summary>Clicking a marriage summary edits the couple's marriage (creating it if needed).</summary>
    [RelayCommand]
    private async Task EditMarriageAsync(FamilyCard card)
    {
        if (_tree.Family(card.FamilyId) is not { } f) return;
        var existing = f.Marriage;
        var e = existing ?? Event.Of("MARR");
        var editor = new EventEditorViewModel(_tree, e, isNew: existing == null, forFamily: true,
                                              existing == null ? "Add marriage details" : "Edit marriage");
        editor.AttachPhotos(e, ResolveMedia, () => PickPhotosAsync("Add photos of the wedding"));
        editor.AttachOwner(f);
        var result = await _dialogs.EditEventAsync(editor);
        if (result == EditorResult.Saved)
            Change("edit marriage", editor.TouchedSources.Prepend<Entity>(f), () =>
            {
                editor.Apply(e);
                if (existing == null) f.Events.Insert(0, e);
                if (e.Tag == "MARR" && f.Status == CoupleStatus.Unspecified) f.Status = CoupleStatus.Married;
            });
        else if (result == EditorResult.Deleted && existing != null)
            Change("delete marriage", new[] { f }, () => f.Events.Remove(existing));
    }
}
