using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>A census to choose in the form.</summary>
public sealed record CensusChoice(CensusRecord Census)
{
    public override string ToString() => Census.Name;
}

/// <summary>One line of the household being entered.</summary>
public sealed partial class CensusLineRow : ObservableObject
{
    public static readonly PersonChoice NotInTree = new(0, "— not in the tree —");
    public static readonly PersonChoice AddNew = new(-1, "+ Add as a new person");

    public CensusLineRow(CensusEntryViewModel form, CensusLine line, IReadOnlyList<PersonChoice> people)
    {
        Form = form;
        People = people;
        _person = people.FirstOrDefault(p => p.Id == line.PersonId) ?? NotInTree;
        (_name, _relationship, _condition, _sex, _age, _birthDate, _occupation, _birthplace, _yearsMarried, _immigrated) =
            (line.Name, line.Relationship, line.Condition, line.Sex, line.Age, line.BirthDate, line.Occupation, line.Birthplace, line.YearsMarried, line.Immigrated);
    }

    public CensusEntryViewModel Form { get; }
    public IReadOnlyList<PersonChoice> People { get; }
    [ObservableProperty] private PersonChoice _person;
    [ObservableProperty] private string _name, _relationship, _condition, _sex, _age, _birthDate, _occupation, _birthplace, _yearsMarried, _immigrated;

    public CensusLine ToLine() => new()
    {
        PersonId = Person.Id > 0 ? Person.Id : null, AddAsNew = Person == AddNew,
        Name = Name, Relationship = Relationship, Condition = Condition, Sex = Sex, Age = Age, BirthDate = BirthDate,
        Occupation = Occupation, Birthplace = Birthplace, YearsMarried = YearsMarried, Immigrated = Immigrated,
    };
}

/// <summary>Enters a whole census household: which census, where, the reference, and a line per person.</summary>
public sealed partial class CensusEntryViewModel : ObservableObject
{
    private readonly FamilyTree _tree;
    private readonly IReadOnlyList<PersonChoice> _people;

    public CensusEntryViewModel(FamilyTree tree, Person head, IReadOnlyList<CensusRecord> censuses, CensusRecord initial, string place)
    {
        _tree = tree;
        Head = head;
        _people = new[] { CensusLineRow.NotInTree, CensusLineRow.AddNew }
            .Concat(tree.People.Values.OrderBy(p => p.PrimaryName.SortKey)
                .Select(p => new PersonChoice(p.Id, p.Lifespan.Length > 0 ? $"{p.DisplayName} ({p.Lifespan})" : p.DisplayName)))
            .ToList();
        Censuses = censuses.Select(c => new CensusChoice(c)).ToList();
        _place = place;
        _census = Censuses.FirstOrDefault(c => c.Census == initial) ?? Censuses[0];
        Fill();
    }

    public Person Head { get; }
    public string Title => $"Census household of {Head.DisplayName}";
    public IReadOnlyList<CensusChoice> Censuses { get; }
    public ObservableCollection<CensusLineRow> Rows { get; } = new();
    [ObservableProperty] private CensusChoice _census;
    [ObservableProperty] private string _place;
    [ObservableProperty] private string _reference = "";
    [ObservableProperty] private bool _addOccupations = true;

    public IReadOnlyList<CensusColumn> Columns => CensusHousehold.Columns(Census.Census);
    public bool ShowRelationship => Columns.Contains(CensusColumn.Relationship);
    public bool ShowCondition => Columns.Contains(CensusColumn.Condition);
    public bool ShowSex => Columns.Contains(CensusColumn.Sex);
    public bool ShowAge => Columns.Contains(CensusColumn.Age);
    public bool ShowBirthDate => Columns.Contains(CensusColumn.BirthDate);
    public bool ShowOccupation => Columns.Contains(CensusColumn.Occupation);
    public bool ShowBirthplace => Columns.Contains(CensusColumn.Birthplace);
    public bool ShowYearsMarried => Columns.Contains(CensusColumn.YearsMarried);
    public bool ShowImmigrated => Columns.Contains(CensusColumn.Immigrated);
    public string CensusNote => Census.Census.Note;

    partial void OnCensusChanged(CensusChoice value)
    {
        foreach (var n in new[] { nameof(Columns), nameof(ShowRelationship), nameof(ShowCondition), nameof(ShowSex), nameof(ShowAge),
                                  nameof(ShowBirthDate), nameof(ShowOccupation), nameof(ShowBirthplace), nameof(ShowYearsMarried),
                                  nameof(ShowImmigrated), nameof(CensusNote) })
            OnPropertyChanged(n);
        Fill();
    }

    /// <summary>Fills the household from the tree for the chosen census (ages as of census day).</summary>
    private void Fill()
    {
        Rows.Clear();
        foreach (var line in CensusHousehold.Prefill(_tree, Head, Census.Census)) Rows.Add(new CensusLineRow(this, line, _people));
    }

    [RelayCommand]
    private void AddLine() => Rows.Add(new CensusLineRow(this, new CensusLine(), _people));

    [RelayCommand]
    private void RemoveLine(CensusLineRow row) => Rows.Remove(row);

    public IReadOnlyList<CensusLine> Lines => Rows.Select(r => r.ToLine()).ToList();
}
