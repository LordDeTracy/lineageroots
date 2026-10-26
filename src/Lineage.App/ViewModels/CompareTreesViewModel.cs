using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed record MatchRow(PersonMatch Match)
{
    public string Title => Match.Mine.DisplayName == Match.Other.DisplayName
        ? Match.Mine.DisplayName : $"{Match.Mine.DisplayName}  ·  {Match.Other.DisplayName}";
    public string Detail => Display.Join(Match.How, Match.Differences.Count == 0 ? "no differences"
        : $"{Match.Differences.Count} difference{(Match.Differences.Count == 1 ? "" : "s")}");
    public IReadOnlyList<FieldDifference> Differences => Match.Differences;
    public bool HasDifferences => Match.Differences.Count > 0;
}

public sealed partial class OtherPersonRow : ObservableObject
{
    public OtherPersonRow(Person p) => Person = p;
    public Person Person { get; }
    public string Name => Person.DisplayName;
    public string Detail => Display.Join(Person.Lifespan, Display.BirthPlace(Person));
    [ObservableProperty] private bool _chosen;
}

/// <summary>
/// Compares the open tree with another file (a relative's GEDCOM, an older copy…): who matches, what
/// differs, and who is only in one of them. Chosen people from the other tree can be copied in;
/// the copy itself is done by the main window so it can be undone.
/// </summary>
public sealed partial class CompareTreesViewModel : ObservableObject
{
    private readonly FamilyTree _mine;
    private readonly Action<long> _open;
    private CompareResult _result;

    public CompareTreesViewModel(FamilyTree mine, FamilyTree other, string otherName, CompareResult result, Action<long> open)
    {
        _mine = mine;
        Other = other;
        OtherName = otherName;
        _result = result;
        _open = open;
        Refresh();
    }

    public FamilyTree Other { get; }
    public string OtherName { get; }
    public CompareResult Result => _result;
    public ObservableCollection<MatchRow> Matches { get; } = new();
    public ObservableCollection<OtherPersonRow> OnlyTheirs { get; } = new();
    public ObservableCollection<PersonCard> OnlyMine { get; } = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsMatchesTab), nameof(IsTheirsTab), nameof(IsMineTab))]
    private int _tabIndex;
    public bool IsMatchesTab => TabIndex == 0;
    public bool IsTheirsTab => TabIndex == 1;
    public bool IsMineTab => TabIndex == 2;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private MatchRow? _selectedMatch;
    public string[] Tabs => new[] { $"In both ({Matches.Count:N0})", $"Only in theirs ({OnlyTheirs.Count:N0})", $"Only in yours ({OnlyMine.Count:N0})" };

    /// <summary>Set by the window's Copy button; read by the main window after it closes.</summary>
    public IReadOnlyList<long> ChosenIds => OnlyTheirs.Where(r => r.Chosen).Select(r => r.Person.Id).ToList();

    private void Refresh()
    {
        Matches.Clear();
        foreach (var m in _result.Matches.OrderByDescending(m => m.Differences.Count).ThenBy(m => m.Mine.PrimaryName.SortKey))
            Matches.Add(new MatchRow(m));
        OnlyTheirs.Clear();
        foreach (var p in _result.OnlyInOther) OnlyTheirs.Add(new OtherPersonRow(p));
        OnlyMine.Clear();
        foreach (var p in _result.OnlyInMine.Take(2000)) OnlyMine.Add(new PersonCard(p));
        Summary = $"{_mine.People.Count:N0} people in yours, {Other.People.Count:N0} in {OtherName}: " +
                  $"{_result.Matches.Count:N0} in both, {_result.OnlyInOther.Count:N0} only in theirs, {_result.OnlyInMine.Count:N0} only in yours.";
        OnPropertyChanged(nameof(Tabs));
        SelectedMatch = Matches.FirstOrDefault();
    }

    [RelayCommand]
    private void NotSamePerson(MatchRow row)
    {
        _result = _result.Without(row.Match);
        Refresh();
    }

    [RelayCommand]
    private void OpenMine(MatchRow row) => _open(row.Match.Mine.Id);

    [RelayCommand]
    private void ChooseAll(bool chosen)
    {
        foreach (var r in OnlyTheirs) r.Chosen = chosen;
    }
}
