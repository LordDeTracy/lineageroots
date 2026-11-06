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

    /// <summary>0 = open tasks, 1 = done, 2 = everything, 3 = suggestions, 4 = search log, 5 = letters.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLettersFilter), nameof(NewResearchText), nameof(IsClientsFilter))]
    private int _researchFilterIndex;
    public bool IsLettersFilter => ResearchFilterIndex == 5;
    public string NewResearchText => IsClientsFilter ? "New client project" : IsLettersFilter ? "New letter" : "New task";
    [ObservableProperty] private string _researchSearch = "";
    [ObservableProperty] private string _researchSummary = "";
    [ObservableProperty] private bool _hasPersonResearch;
    public bool HasNoResearch => _tree.Research.Count == 0 && !IsSuggestedFilter && !IsClientsFilter;
    public bool HasResearchResults => ResearchList.Count > 0;
    public bool ShowNoResearchMatches => !HasNoResearch && ResearchList.Count == 0 && !IsSuggestedFilter && !IsClientsFilter;

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
        var detail = r.Letter is { } l
            ? Display.Join($"{l.Method} {(l.Incoming ? "from" : "to")} {(l.With.Length > 0 ? l.With : "someone")}",
                           r.Date.IsEmpty ? "" : $"{(l.Incoming ? "received" : "sent")} {Display.Date(r.Date)}",
                           !l.Replied.IsEmpty ? $"answered {Display.Date(l.Replied)}" : r.Done ? "answered" : l.Incoming ? "to answer" : "awaiting a reply")
            : Display.Join(ResultText(r.Result), r.Category, where, Display.Date(r.Date));
        return new ResearchRow(r.Id, r.Description.Length > 0 ? r.Description : "(no description)",
            detail,
            ResearchEditorViewModel.Priorities[Math.Clamp(r.Priority, 0, 3)], Math.Clamp(r.Priority, 0, 3), r.Done,
            (people ?? new()).Select(p => new PersonChoice(p.Id, p.DisplayName)).ToList(),
            r.Note.Length > 140 ? r.Note[..140].TrimEnd() + "…" : r.Note);
    }

    private static string ResultText(SearchResult r) => r switch
    {
        SearchResult.Found => "Searched: found",
        SearchResult.NotFound => "Searched: not found",
        SearchResult.Inconclusive => "Searched: unclear",
        _ => "",
    };

    private void RebuildResearch()
    {
        OnPropertyChanged(nameof(IsSuggestedFilter));
        RebuildProjects();
        if (IsClientsFilter)
        {
            ResearchList.Clear();
            OnPropertyChanged(nameof(HasResearchResults));
            OnPropertyChanged(nameof(ShowNoResearchMatches));
            return;
        }
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
        var isLog = ResearchFilterIndex is 4 or 5;
        var rows = _tree.Research.Values
            .Where(r => IsLettersFilter ? r.Letter != null
                        : r.Letter == null && (isLog ? r.Result != SearchResult.None
                        : r.Result == SearchResult.None && (ResearchFilterIndex == 2 || r.Done == (ResearchFilterIndex == 1))))
            .OrderByDescending(r => isLog ? r.Date.SortKey : 0)   // the search log and letters run newest first
            .Select(r => ToRow(r, people.GetValueOrDefault(r.Id)))
            .Where(row => words.All(w => row.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
                                      || row.Detail.Contains(w, StringComparison.OrdinalIgnoreCase)
                                      || row.People.Any(p => p.Text.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(row => !isLog && row.Done).ThenBy(row => isLog || row.PriorityLevel == 0 ? 9 : row.PriorityLevel)
            .ThenBy(row => isLog ? "" : row.Description)
            .ToList();
        ResearchList.Clear();
        foreach (var row in rows) ResearchList.Add(row);
        var open = _tree.Research.Values.Count(r => !r.Done);
        var searches = _tree.Research.Values.Count(r => r.Result != SearchResult.None);
        ResearchSummary = _tree.Research.Count == 0 ? "" : $"{open:N0} open · {_tree.Research.Count - open - searches:N0} done"
            + (searches > 0 ? $" · {searches:N0} search{(searches == 1 ? "" : "es")} logged" : "");
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
    private Task NewResearchAsync() => IsClientsFilter ? NewProjectAsync() : IsLettersFilter ? NewLetterAsync() : EditResearchItemAsync(null, Array.Empty<long>());

    /// <summary>Logs a letter, email or call to or from an archive or relative.</summary>
    [RelayCommand]
    private Task NewLetterAsync() => EditResearchItemAsync(null, Array.Empty<long>(),
        new ResearchItem { Letter = new Correspondence(), Date = GenDate.Parse(DateTime.Today.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)) });

    /// <summary>A checklist for a visit to one archive: tasks to do there, records held there, letters waiting on them.</summary>
    [RelayCommand]
    private async Task PlanTripAsync()
    {
        var archives = _tree.Repositories.Values.Where(r => r.Name.Trim().Length > 0)
            .GroupBy(r => r.Name.Trim(), StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);
        if (archives.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Plan an archive trip",
                "There are no archives or libraries in the tree yet. Add one to a source (Where it is kept) or to a research task first.");
            return;
        }
        var name = await _dialogs.PromptAsync("Plan an archive trip", "Which archive or library are you visiting?",
            archives.Count == 1 ? archives.Keys.First() : "", archives.Keys.OrderBy(k => k).ToList());
        if (name == null) return;
        if (!archives.TryGetValue(name.Trim(), out var repo))
        {
            await _dialogs.ShowMessageAsync("Plan an archive trip", $"There's no archive called \"{name.Trim()}\" in the tree.");
            return;
        }
        await SaveDocumentAsync($"Visit to {repo.Name.Trim()}", () => Lineage.Core.Reports.Narrative.TripPlan(_tree, repo));
    }

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

    /// <param name="draft">A new item that starts filled in (a letter); it's added like any new task.</param>
    private async Task EditResearchItemAsync(ResearchItem? existing, IEnumerable<long> linked, ResearchItem? draft = null)
    {
        var item = existing ?? draft ?? new ResearchItem();
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
