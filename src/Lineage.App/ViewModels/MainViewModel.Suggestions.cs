using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>A research suggestion for one person, as shown on their page or the Research page.</summary>
public sealed record HintRow(ResearchHint Hint, long PersonId, string PersonName, bool HasTask, bool ShowPerson = false)
{
    public string Task => Hint.Task;
    public string Why => Hint.Why;
    public string Where => Hint.Where;
    public bool HasWhere => Where.Length > 0;
    public string Category => Hint.Category;
    public bool IsHigh => Hint.Priority == 1;
    public bool CanAdd => !HasTask;
}

/// <summary>Suggested next steps: per person on their page, and for the home person's ancestors on the Research page.</summary>
public sealed partial class MainViewModel
{
    private const int HintsShown = 4, AncestorGenerations = 6, MaxSuggestedRows = 200;

    public ObservableCollection<HintRow> PersonHints { get; } = new();
    public ObservableCollection<HintRow> SuggestedResearch { get; } = new();

    [ObservableProperty] private bool _showAllHints;
    [ObservableProperty] private bool _hasPersonHints;
    [ObservableProperty] private string _moreHintsText = "";
    [ObservableProperty] private string _suggestedSummary = "";

    public bool IsSuggestedFilter => ResearchFilterIndex == 3;
    public bool HasMoreHints => MoreHintsText.Length > 0;

    partial void OnShowAllHintsChanged(bool value)
    {
        if (_current is { } p) RebuildPersonHints(p);
    }

    private void RebuildPersonHints(Person p)
    {
        PersonHints.Clear();
        var all = ResearchSuggestions.For(_tree, p);
        var tasks = PersonTaskDescriptions(p);
        foreach (var h in ShowAllHints ? all : all.Take(HintsShown))
            PersonHints.Add(new HintRow(h, p.Id, p.DisplayName, tasks.Contains(h.Task)));
        HasPersonHints = PersonHints.Count > 0;
        MoreHintsText = all.Count > HintsShown ? (ShowAllHints ? "Show fewer" : $"Show all {all.Count}") : "";
        OnPropertyChanged(nameof(HasMoreHints));
    }

    [RelayCommand] private void ToggleAllHints() => ShowAllHints = !ShowAllHints;

    /// <summary>
    /// The most useful suggestions (high and medium priority) for the home person — or whoever is showing — and
    /// their ancestors, nearest generations first.
    /// </summary>
    private void RebuildSuggestedResearch()
    {
        SuggestedResearch.Clear();
        var root = _tree.Person(_tree.HomePersonId) ?? _current;
        if (root == null) { SuggestedSummary = ""; return; }
        var total = 0;
        foreach (var (_, person) in _tree.Ancestors(root, AncestorGenerations).OrderBy(kv => kv.Key))
        {
            var tasks = PersonTaskDescriptions(person);
            foreach (var h in ResearchSuggestions.For(_tree, person).Where(h => h.Priority <= 2).Take(3))
            {
                total++;
                if (SuggestedResearch.Count < MaxSuggestedRows)
                    SuggestedResearch.Add(new HintRow(h, person.Id, person.DisplayName, tasks.Contains(h.Task), ShowPerson: true));
            }
        }
        var who = _tree.HomePersonId == root.Id ? $"{root.DisplayName} (the home person)" : root.DisplayName;
        SuggestedSummary = total == 0
            ? $"No suggestions for {who} and their ancestors — nicely done."
            : $"Next steps for {who} and their ancestors, {AncestorGenerations} generations back, nearest first"
              + (total > SuggestedResearch.Count ? $" (the first {SuggestedResearch.Count:N0} of {total:N0})." : ".");
    }

    [RelayCommand]
    private void AddHintTask(HintRow row)
    {
        if (row.HasTask || _tree.Person(row.PersonId) is not { } p) return;
        var item = new ResearchItem
        {
            Description = row.Task,
            Category = row.Category,
            Priority = row.Hint.Priority,   // 1 high, 2 medium, 3 low — the same scale as tasks
            Note = row.HasWhere ? $"{row.Why}\n{row.Where}" : row.Why,
        };
        Change("add research task", new Entity[] { p, item }, () =>
        {
            _tree.AddResearch(item);
            p.ResearchIds.Add(item.Id);
        });
        if (IsResearchSection) RebuildSuggestedResearch();
    }

    [RelayCommand]
    private void DismissHint(HintRow row)
    {
        if (_tree.Person(row.PersonId) is not { } p) return;
        Change("set aside suggestion", new[] { p }, () => p.DismissedHints.Add(row.Hint.Key));
        if (IsResearchSection) RebuildSuggestedResearch();
    }

    [RelayCommand]
    private void OpenHintPerson(HintRow row) => OpenPerson(row.PersonId);
}
