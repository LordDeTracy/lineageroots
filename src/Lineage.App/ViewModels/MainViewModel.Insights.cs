using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>Home page insights, the problem finder and the relationship calculator.</summary>
public sealed partial class MainViewModel
{
    partial void RebuildHomeInsights()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Anniversaries.Clear();
        foreach (var a in Dashboard.Anniversaries(_tree, today, 30).Take(8))
        {
            var p = _tree.Person(a.PersonId);
            if (p == null) continue;
            var spouse = _tree.Person(a.SpouseId);
            var text = a.Kind switch
            {
                AnniversaryKind.Birth => $"{p.DisplayName} was born",
                AnniversaryKind.Marriage => $"{p.DisplayName} married {spouse?.DisplayName ?? "their spouse"}",
                _ => $"{p.DisplayName} died",
            };
            Anniversaries.Add(new HomeLine(a.On == today ? "Today" : a.On.ToString("d MMM"), text,
                $"{a.YearsAgo} years ago, in {a.Year}", p.Id));
        }
        HasAnniversaries = Anniversaries.Count > 0;

        var missing = Dashboard.MissingData(_tree);
        HealthLines.Clear();
        void Line(int count, string what, IReadOnlyList<long> ids)
        {
            if (count > 0) HealthLines.Add(new HomeLine(count.ToString("N0"), what, ids.Count > 0 && _tree.Person(ids[0]) is { } first ? $"For example {first.DisplayName}" : "", ids.FirstOrDefault(), ids));
        }
        Line(missing.NoBirthDateCount, missing.NoBirthDateCount == 1 ? "person has no birth date" : "people have no birth date", missing.NoBirthDate);
        Line(missing.NoCitationsCount, missing.NoCitationsCount == 1 ? "person has no sources" : "people have no sources", missing.NoCitations);
        Line(missing.NoParentsCount, missing.NoParentsCount == 1 ? "person has no parents recorded" : "people have no parents recorded", missing.NoParents);
        Line(missing.EventsWithoutPlaceCount, missing.EventsWithoutPlaceCount == 1 ? "person has events without a place" : "people have events without a place", missing.EventsWithoutPlace);
        HasHealthLines = _tree.People.Count > 0;

        var stats = Dashboard.Stats(_tree);
        HomeStatsText = Display.Join($"{stats.People:N0} people", $"{stats.Families:N0} families", $"{_tree.Sources.Count:N0} sources",
            $"{stats.Surnames:N0} surnames", stats.EarliestBirthYear is { } y ? $"back to {y}" : "",
            stats.AverageLifespan is { } life ? $"average lifespan {life:0} years" : "");
        KeyFigures.Clear();
        KeyFigures.Add(new KeyFigure($"{stats.People:N0}", stats.People == 1 ? "person" : "people"));
        KeyFigures.Add(new KeyFigure($"{stats.Families:N0}", stats.Families == 1 ? "family" : "families"));
        KeyFigures.Add(new KeyFigure($"{_tree.Sources.Count:N0}", _tree.Sources.Count == 1 ? "source" : "sources"));
        KeyFigures.Add(new KeyFigure($"{stats.Surnames:N0}", stats.Surnames == 1 ? "surname" : "surnames"));
        if (stats.EarliestBirthYear is { } first) KeyFigures.Add(new KeyFigure(first.ToString(), "earliest birth"));
        if (stats.AverageLifespan is { } years) KeyFigures.Add(new KeyFigure($"{years:0}", "average lifespan"));
        TopSurnames.Clear();
        foreach (var s in stats.TopSurnames) TopSurnames.Add($"{s.Surname}  {s.Count:N0}");
        HasTopSurnames = TopSurnames.Count > 0;
    }

    [RelayCommand]
    private void FindProblems()
    {
        if (!HasTree) return;
        _dialogs.ShowProblems(new ProblemsViewModel(() => _tree, id => { OpenPerson(id); }));
    }

    [RelayCommand]
    private void FindRelationship()
    {
        if (!HasTree) return;
        _dialogs.ShowRelationship(new RelationshipViewModel(_tree, _current?.Id, id => OpenPerson(id)));
    }

    /// <summary>How many problems the finder sees, shown at the top of the Research tab.</summary>
    [ObservableProperty] private string _problemsSummary = "";

    private async void RefreshProblemsSummary()
    {
        var tree = _tree;
        ProblemsSummary = "Checking…";
        var found = await Task.Run(() => Problems.Find(tree));
        if (!ReferenceEquals(tree, _tree)) return;
        var errors = found.Count(p => p.Severity == ProblemSeverity.Error);
        var warnings = found.Count(p => p.Severity == ProblemSeverity.Warning);
        ProblemsSummary = errors + warnings == 0
            ? "No impossible or unlikely dates or relationships found."
            : $"{errors:N0} impossible · {warnings:N0} unlikely. Open the finder to go through them.";
    }

    /// <summary>The people list search: plain matching, or "sounds like" when the toggle is on.</summary>
    private IEnumerable<long> SearchIds(string query) => NameSearch.Find(_tree, query, SoundsLike).Select(p => p.Id);
}
