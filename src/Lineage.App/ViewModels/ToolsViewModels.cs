using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed record ProblemRow(Problem Problem, string PersonName)
{
    public string Message => Problem.Message;
    public bool IsError => Problem.Severity == ProblemSeverity.Error;
    public bool IsWarning => Problem.Severity == ProblemSeverity.Warning;
    public bool IsInfo => Problem.Severity == ProblemSeverity.Info;
    public string SeverityText => Problem.Severity switch
    {
        ProblemSeverity.Error => "Impossible",
        ProblemSeverity.Warning => "Unlikely",
        _ => "Worth a look",
    };
}

/// <summary>
/// The problem finder: dates and relationships that look impossible or unlikely. The window stays
/// open while the user fixes things; Check again re-runs the scan.
/// </summary>
public sealed partial class ProblemsViewModel : ObservableObject
{
    private readonly Func<FamilyTree> _tree;
    private readonly Action<long> _open;
    private List<ProblemRow> _all = new();

    public ProblemsViewModel(Func<FamilyTree> tree, Action<long> open)
    {
        _tree = tree;
        _open = open;
        Refresh();
    }

    public ObservableCollection<ProblemRow> Rows { get; } = new();
    [ObservableProperty] private int _filterIndex;   // 0 impossible + unlikely, 1 impossible, 2 everything
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private ProblemRow? _selected;
    public bool IsEmpty => Rows.Count == 0;

    partial void OnFilterIndexChanged(int value) => Apply();
    partial void OnSelectedChanged(ProblemRow? value) { if (value != null) _open(value.Problem.PersonId); }

    [RelayCommand]
    private void Refresh()
    {
        var tree = _tree();
        _all = Problems.Find(tree).Select(p => new ProblemRow(p, tree.Person(p.PersonId)?.DisplayName ?? $"Person {p.PersonId}"))
                       .OrderBy(r => r.Problem.Severity).ThenBy(r => r.PersonName).ToList();
        Apply();
    }

    private void Apply()
    {
        Rows.Clear();
        foreach (var r in _all.Where(r => FilterIndex switch
                 {
                     0 => r.Problem.Severity != ProblemSeverity.Info,
                     1 => r.Problem.Severity == ProblemSeverity.Error,
                     _ => true,
                 }))
            Rows.Add(r);
        var errors = _all.Count(r => r.IsError);
        var warnings = _all.Count(r => r.IsWarning);
        var info = _all.Count(r => r.IsInfo);
        Summary = _all.Count == 0 ? "No problems found." : $"{errors:N0} impossible · {warnings:N0} unlikely · {info:N0} worth a look";
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed record PathStep(long Id, string Name, string Relation);

/// <summary>"How are these two related?" — pick two people, see the relationship and the line between them.</summary>
public sealed partial class RelationshipViewModel : ObservableObject
{
    private readonly FamilyTree _tree;
    private readonly Action<long> _open;

    public RelationshipViewModel(FamilyTree tree, long? first, Action<long> open)
    {
        _tree = tree;
        _open = open;
        People = tree.People.Values.OrderBy(p => p.PrimaryName.SortKey)
            .Select(p => new PersonChoice(p.Id, p.Lifespan.Length > 0 ? $"{p.DisplayName} ({p.Lifespan})" : p.DisplayName)).ToList();
        _first = People.FirstOrDefault(p => p.Id == first);
        var home = tree.Person(tree.HomePersonId);
        _second = home != null && home.Id != first ? People.FirstOrDefault(p => p.Id == home.Id) : null;
        Calculate();
    }

    public IReadOnlyList<PersonChoice> People { get; }
    public ObservableCollection<PathStep> Path { get; } = new();
    [ObservableProperty] private PersonChoice? _first;
    [ObservableProperty] private PersonChoice? _second;
    [ObservableProperty] private string _answer = "";
    [ObservableProperty] private string _explanation = "";
    public bool HasPath => Path.Count > 0;

    partial void OnFirstChanged(PersonChoice? value) => Calculate();
    partial void OnSecondChanged(PersonChoice? value) => Calculate();

    [RelayCommand]
    private void Swap() => (First, Second) = (Second, First);

    [RelayCommand]
    private void Open(PathStep step) => _open(step.Id);

    private void Calculate()
    {
        Path.Clear();
        if (_tree.Person(First?.Id) is not { } a || _tree.Person(Second?.Id) is not { } b)
        {
            Answer = "";
            Explanation = "Choose two people to see how they're related.";
            OnPropertyChanged(nameof(HasPath));
            return;
        }
        var r = Relationship.Describe(_tree, a, b);
        Answer = r.Related && r.Text != Relationship.SamePerson
            ? $"{b.DisplayName} is {Possessive(a.DisplayName)} {r.Text}."
            : r.Text;
        var common = r.CommonAncestors.Select(id => _tree.Person(id)?.DisplayName).OfType<string>().ToList();
        Explanation = common.Count > 0 ? $"Their closest common ancestor{(common.Count > 1 ? "s are" : " is")} {string.Join(" and ", common)}."
                    : r.ByMarriage ? "Related by marriage." : "";
        foreach (var id in r.Path)
            if (_tree.Person(id) is { } p) Path.Add(new PathStep(p.Id, p.DisplayName, p.Lifespan));
        OnPropertyChanged(nameof(HasPath));
    }

    private static string Possessive(string name) => name.EndsWith('s') ? name + "'" : name + "'s";
}
