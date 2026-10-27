using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One research task in a list.</summary>
public sealed record ResearchRow(long Id, string Description, string Detail, string Priority, int PriorityLevel, bool Done,
                                 IReadOnlyList<PersonChoice> People, string Note)
{
    public bool HasDetail => Detail.Length > 0;
    public bool HasPriority => PriorityLevel > 0;
    public bool IsHigh => PriorityLevel == 1;
    public bool HasPeople => People.Count > 0;
    public bool HasNote => Note.Length > 0;
}

public sealed partial class MainViewModel
{
    public ObservableCollection<ResearchRow> ResearchList { get; } = new();
    public ObservableCollection<ResearchRow> PersonResearch { get; } = new();

    /// <summary>0 = open tasks, 1 = done, 2 = everything, 3 = suggestions.</summary>
    [ObservableProperty] private int _researchFilterIndex;
    [ObservableProperty] private string _researchSearch = "";
    [ObservableProperty] private string _researchSummary = "";
    [ObservableProperty] private bool _hasPersonResearch;
    public bool HasNoResearch => _tree.Research.Count == 0 && !IsSuggestedFilter;
    public bool HasResearchResults => ResearchList.Count > 0;
    public bool ShowNoResearchMatches => !HasNoResearch && ResearchList.Count == 0 && !IsSuggestedFilter;

    partial void OnResearchFilterIndexChanged(int value) => RebuildResearch();
    partial void OnResearchSearchChanged(string value) => RebuildResearch();

    /// <summary>Research id → the people it is about (directly, or as a partner in a linked family).</summary>
    private Dictionary<long, List<Person>> ResearchPeople()
    {
        var map = new Dictionary<long, List<Person>>();
        void Add(long id, Person p)
        {
            if (!map.TryGetValue(id, out var list)) map[id] = list = new();
            if (!list.Contains(p)) list.Add(p);
        }
        foreach (var p in _tree.People.Values)
            foreach (var id in p.ResearchIds) Add(id, p);
        foreach (var f in _tree.Families.Values)
            foreach (var id in f.ResearchIds)
                foreach (var pid in f.PartnerIds)
                    if (_tree.Person(pid) is { } partner) Add(id, partner);
        return map;
    }

    private ResearchRow ToRow(ResearchItem r, List<Person>? people)
    {
        var where = r.SourceId is { } sid && _tree.Sources.TryGetValue(sid, out var s) ? s.Title
                  : r.RepositoryId is { } rid && _tree.Repositories.TryGetValue(rid, out var repo) ? repo.Name : "";
        return new ResearchRow(r.Id, r.Description.Length > 0 ? r.Description : "(no description)",
            Display.Join(r.Category, where, Display.Date(r.Date)),
            ResearchEditorViewModel.Priorities[Math.Clamp(r.Priority, 0, 3)], Math.Clamp(r.Priority, 0, 3), r.Done,
            (people ?? new()).Select(p => new PersonChoice(p.Id, p.DisplayName)).ToList(),
            r.Note.Length > 140 ? r.Note[..140].TrimEnd() + "…" : r.Note);
    }

    private void RebuildResearch()
    {
        OnPropertyChanged(nameof(IsSuggestedFilter));
        if (IsSuggestedFilter)
        {
            ResearchList.Clear();
            RebuildSuggestedResearch();
            OnPropertyChanged(nameof(HasNoResearch));
            OnPropertyChanged(nameof(HasResearchResults));
            OnPropertyChanged(nameof(ShowNoResearchMatches));
            return;
        }
        var people = ResearchPeople();
        var words = ResearchSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rows = _tree.Research.Values
            .Where(r => ResearchFilterIndex == 2 || r.Done == (ResearchFilterIndex == 1))
            .Select(r => ToRow(r, people.GetValueOrDefault(r.Id)))
            .Where(row => words.All(w => row.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
                                      || row.Detail.Contains(w, StringComparison.OrdinalIgnoreCase)
                                      || row.People.Any(p => p.Text.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(row => row.Done).ThenBy(row => row.PriorityLevel == 0 ? 9 : row.PriorityLevel).ThenBy(row => row.Description)
            .ToList();
        ResearchList.Clear();
        foreach (var row in rows) ResearchList.Add(row);
        var open = _tree.Research.Values.Count(r => !r.Done);
        ResearchSummary = _tree.Research.Count == 0 ? "" : $"{open:N0} open · {_tree.Research.Count - open:N0} done";
        OnPropertyChanged(nameof(HasNoResearch));
        OnPropertyChanged(nameof(HasResearchResults));
        OnPropertyChanged(nameof(ShowNoResearchMatches));
    }

    private void RebuildPersonResearch(Person p)
    {
        PersonResearch.Clear();
        var ids = p.ResearchIds.Concat(p.SpouseFamilyIds.Select(id => _tree.Family(id)).OfType<Family>().SelectMany(f => f.ResearchIds)).Distinct();
        var people = ResearchPeople();
        foreach (var id in ids)
            if (_tree.Research.TryGetValue(id, out var r)) PersonResearch.Add(ToRow(r, people.GetValueOrDefault(id)));
        HasPersonResearch = PersonResearch.Count > 0;
    }

    [RelayCommand]
    private Task NewResearchAsync() => EditResearchItemAsync(null, Array.Empty<long>());

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private Task AddResearchForPersonAsync() => _current is { } p ? EditResearchItemAsync(null, new[] { p.Id }) : Task.CompletedTask;

    [RelayCommand]
    private Task EditResearchAsync(ResearchRow row) =>
        _tree.Research.TryGetValue(row.Id, out var item) ? EditResearchItemAsync(item, row.People.Select(p => p.Id)) : Task.CompletedTask;

    [RelayCommand]
    private void ToggleResearchDone(ResearchRow row)
    {
        if (!_tree.Research.TryGetValue(row.Id, out var item)) return;
        Change(item.Done ? "reopen task" : "finish task", new[] { item }, () => item.Done = !item.Done);
    }

    private async Task EditResearchItemAsync(ResearchItem? existing, IEnumerable<long> linked)
    {
        var item = existing ?? new ResearchItem();
        var before = linked.ToList();
        var editor = new ResearchEditorViewModel(_tree, item, isNew: existing == null, before);
        var result = await _dialogs.EditResearchAsync(editor);
        if (result == EditorResult.Cancelled) return;

        // Everyone whose task list may change: people linked before and after.
        var after = editor.PersonIds;
        var touched = before.Union(after).Select(id => _tree.Person(id)).OfType<Entity>()
            .Concat(_tree.Families.Values.Where(f => existing != null && f.ResearchIds.Contains(existing.Id)))
            .Append(item).ToList();

        if (result == EditorResult.Deleted && existing != null)
        {
            Change("delete research task", touched, () =>
            {
                foreach (var p in _tree.People.Values) p.ResearchIds.Remove(existing.Id);
                foreach (var f in _tree.Families.Values) f.ResearchIds.Remove(existing.Id);
                _tree.Research.Remove(existing.Id);
            });
            return;
        }

        Change(existing == null ? "add research task" : "edit research task", touched, () =>
        {
            editor.Apply(item);
            if (existing == null) _tree.AddResearch(item);
            foreach (var id in before.Except(after))
                _tree.Person(id)?.ResearchIds.Remove(item.Id);
            foreach (var id in after)
                if (_tree.Person(id) is { } p && !p.ResearchIds.Contains(item.Id)) p.ResearchIds.Add(item.Id);
        });
    }
}
