using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lineage.App.ViewModels;

/// <summary>One line on the Home page: a short badge (date, count), text, detail, and who it's about.</summary>
public sealed record HomeLine(string Badge, string Text, string Detail, long? PersonId)
{
    public bool HasDetail => Detail.Length > 0;
    public bool HasPerson => PersonId != null;
}

/// <summary>The Home page: an overview of the tree and what to do next.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<ResearchRow> HomeResearch { get; } = new();
    public ObservableCollection<HomeLine> Anniversaries { get; } = new();
    public ObservableCollection<HomeLine> HealthLines { get; } = new();
    public ObservableCollection<string> TopSurnames { get; } = new();

    [ObservableProperty] private string _homeStatsText = "";
    [ObservableProperty] private bool _hasHomeResearch;
    [ObservableProperty] private bool _hasAnniversaries;
    [ObservableProperty] private bool _hasHealthLines;
    [ObservableProperty] private bool _hasTopSurnames;

    private void RebuildHome()
    {
        RefreshBookmarkState();

        HomeResearch.Clear();
        var people = ResearchPeople();
        foreach (var r in _tree.Research.Values.Where(r => !r.Done)
                     .OrderBy(r => r.Priority == 0 ? 9 : r.Priority).ThenBy(r => r.Description).Take(5))
            HomeResearch.Add(ToRow(r, people.GetValueOrDefault(r.Id)));
        HasHomeResearch = HomeResearch.Count > 0;

        HomeStatsText = Display.Join($"{_tree.People.Count:N0} people", $"{_tree.Families.Count:N0} families",
                                     $"{_tree.Sources.Count:N0} sources");
        RebuildHomeInsights();
    }

    /// <summary>Anniversaries, tree health and surnames — filled in from the analysis code.</summary>
    partial void RebuildHomeInsights();

    [RelayCommand]
    private void ShowSection(AppSection section) => Section = section;
}
