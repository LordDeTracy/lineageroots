using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One line in the sources list.</summary>
public sealed record SourceRow(long Id, string Title, string Subtitle)
{
    public bool HasSubtitle => Subtitle.Length > 0;
    public override string ToString() => Title;
}

/// <summary>Someone who cites the open source, and what for.</summary>
public sealed record CiterRow(long PersonId, string Name, string What);

public sealed partial class MainViewModel
{
    private List<SourceRow> _allSources = new();
    private Dictionary<long, int> _citationCounts = new();

    public ObservableCollection<SourceRow> SourceList { get; } = new();
    public ObservableCollection<CiterRow> CitedBy { get; } = new();

    [ObservableProperty] private SourceRow? _selectedSource;
    [ObservableProperty] private string _sourceCountText = "";
    [ObservableProperty] private string _sourceTitle = "";
    [ObservableProperty] private string _sourceDetail = "";
    [ObservableProperty] private string _sourceRepository = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _sourceNote = "";
    [ObservableProperty] private string _citedByHeading = "";

    public bool HasSelectedSource => SelectedSource != null;
    public bool HasSourceRepository => SourceRepository.Length > 0;
    public bool HasSourceText => SourceText.Trim().Length > 0;
    public bool HasSourceNote => SourceNote.Trim().Length > 0;
    public bool HasNoSources => _allSources.Count == 0;

    partial void OnSelectedSourceChanged(SourceRow? value) => ShowSource();

    private void RebuildSources()
    {
        _citationCounts = new Dictionary<long, int>();
        foreach (var list in _tree.CitationLists())
            foreach (var c in list)
                _citationCounts[c.SourceId] = _citationCounts.GetValueOrDefault(c.SourceId) + 1;
        _allSources = _tree.Sources.Values
            .Select(s => new SourceRow(s.Id, s.Title.Length > 0 ? s.Title : $"Source {s.Id}",
                Display.Join(s.Author, _citationCounts.GetValueOrDefault(s.Id) is var n and > 0 ? $"{n} citation{(n == 1 ? "" : "s")}" : "Not cited")))
            .OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        ApplySourceFilter();
        OnPropertyChanged(nameof(HasNoSources));
    }

    private void ApplySourceFilter()
    {
        var keep = SelectedSource?.Id;
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        SourceList.Clear();
        foreach (var r in _allSources.Where(r => words.All(w =>
                     r.Title.Contains(w, StringComparison.OrdinalIgnoreCase) || r.Subtitle.Contains(w, StringComparison.OrdinalIgnoreCase))))
            SourceList.Add(r);
        SourceCountText = SourceList.Count == _allSources.Count
            ? $"{_allSources.Count:N0} SOURCE{(_allSources.Count == 1 ? "" : "S")}"
            : $"{SourceList.Count:N0} of {_allSources.Count:N0}";
        SelectedSource = SourceList.FirstOrDefault(r => r.Id == keep) ?? SourceList.FirstOrDefault();
        ShowSource();
    }

    private void ShowSource()
    {
        OnPropertyChanged(nameof(HasSelectedSource));
        CitedBy.Clear();
        if (SelectedSource is not { } row || !_tree.Sources.TryGetValue(row.Id, out var s))
        {
            SourceTitle = SourceDetail = SourceRepository = SourceText = SourceNote = CitedByHeading = "";
            NotifySourcePage();
            return;
        }
        SourceTitle = row.Title;
        SourceDetail = Display.Join(s.Author.Length > 0 ? $"By {s.Author}" : "", s.Publication,
                                    s.CallNumber.Length > 0 ? $"Call number {s.CallNumber}" : "", s.Type);
        var repo = s.RepositoryId is { } rid ? _tree.Repositories.GetValueOrDefault(rid) : null;
        SourceRepository = repo == null ? "" : Display.Join(repo.Name, repo.Address.Replace("\n", ", "), repo.Phone);
        SourceText = s.Text;
        SourceNote = s.Note;

        foreach (var p in _tree.People.Values)
        {
            var what = p.Events.Where(e => e.Citations.Any(c => c.SourceId == s.Id)).Select(EventTypes.Label).ToList();
            if (p.Citations.Any(c => c.SourceId == s.Id)) what.Insert(0, "Person");
            foreach (var f in p.SpouseFamilyIds.Select(id => _tree.Family(id)).OfType<Family>().Where(f => f.Partner1Id == p.Id || f.Partner2Id == null))
                what.AddRange(f.Events.Where(e => e.Citations.Any(c => c.SourceId == s.Id)).Select(EventTypes.Label));
            if (what.Count > 0) CitedBy.Add(new CiterRow(p.Id, p.DisplayName, string.Join("  ·  ", what.Distinct())));
            if (CitedBy.Count >= 500) break;
        }
        ShowSourcePhotos(s);
        CitedByHeading = CitedBy.Count == 0 ? "Not cited yet — add it to an event from the person's page" : $"CITED FOR {CitedBy.Count:N0} {(CitedBy.Count == 1 ? "PERSON" : "PEOPLE")}";
        NotifySourcePage();
    }

    private void NotifySourcePage()
    {
        OnPropertyChanged(nameof(HasSourceRepository));
        OnPropertyChanged(nameof(HasSourceText));
        OnPropertyChanged(nameof(HasSourceNote));
        EditSourceCommand.NotifyCanExecuteChanged();
        AddSourcePhotosCommand.NotifyCanExecuteChanged();
        DeleteSourceCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        var s = new Source();
        var editor = new SourceEditorViewModel(_tree, s, isNew: true);
        if (await _dialogs.EditSourceAsync(editor) != EditorResult.Saved) return;
        Change("add source", Array.Empty<Entity>(), () => { editor.Apply(s); _tree.AddSource(s); });
        Section = AppSection.Sources;
        SelectedSource = SourceList.FirstOrDefault(r => r.Id == s.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSource))]
    private async Task EditSourceAsync()
    {
        if (SelectedSource is not { } row || !_tree.Sources.TryGetValue(row.Id, out var s)) return;
        var editor = new SourceEditorViewModel(_tree, s, isNew: false);
        var result = await _dialogs.EditSourceAsync(editor);
        var repo = s.RepositoryId is { } rid ? _tree.Repositories.GetValueOrDefault(rid) : null;
        if (result == EditorResult.Saved)
            Change("edit source", new Entity?[] { s, repo }.OfType<Entity>(), () => editor.Apply(s));
        else if (result == EditorResult.Deleted)
            await DeleteSourceAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSource))]
    private async Task DeleteSourceAsync()
    {
        if (SelectedSource is not { } row || !_tree.Sources.TryGetValue(row.Id, out var s)) return;
        var citers = _tree.CitersOf(s.Id).ToList();
        var message = citers.Count == 0 ? $"Delete the source \"{row.Title}\"?"
            : $"Delete the source \"{row.Title}\"? It is cited by {citers.Count:N0} records; those citations will be removed too. You can undo this with Ctrl+Z.";
        if (!await _dialogs.ConfirmAsync("Delete source", message)) return;
        Change("delete source", citers.Append(s), () => _tree.RemoveSource(s));
    }
}
