using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One person named in the record being transcribed.</summary>
public sealed partial class RecordLineRow : ObservableObject
{
    public RecordLineRow(TranscribeViewModel form, string role, PersonChoice person, string name)
    {
        Form = form;
        _role = role;
        _person = person;
        _name = name;
    }

    public TranscribeViewModel Form { get; }
    [ObservableProperty] private string _role;
    [ObservableProperty] private PersonChoice _person;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _sex = "";
    [ObservableProperty] private string _age = "";
    [ObservableProperty] private string _residence = "";
    [ObservableProperty] private string _occupation = "";
    [ObservableProperty] private string _note = "";

    /// <summary>Picking someone from the tree fills in their name when none was typed.</summary>
    partial void OnPersonChanged(PersonChoice value)
    {
        if (value.Id > 0 && Name.Trim().Length == 0) Name = Form.NameOf(value.Id);
    }

    public RecordLine ToLine() => new()
    {
        PersonId = Person.Id > 0 ? Person.Id : null, AddAsNew = Person == CensusLineRow.AddNew,
        Name = Name, Role = Role, Sex = Sex, Age = Age, Residence = Residence, Occupation = Occupation, Note = Note,
    };
}

/// <summary>
/// The transcription form: pick the kind of record (baptism, marriage, burial, will, deed), where it is, its dates
/// and place, and a line for each person named in it with their role. Saving turns it into facts and citations.
/// </summary>
public sealed partial class TranscribeViewModel : ObservableObject
{
    private readonly FamilyTree _tree;
    private readonly IReadOnlyList<PersonChoice> _people;

    public static IReadOnlyList<string> Kinds { get; } =
        Enum.GetValues<RecordKind>().Select(RecordTranscription.Label).ToList();

    public TranscribeViewModel(FamilyTree tree, Person? principal)
    {
        _tree = tree;
        _people = new[] { CensusLineRow.NotInTree, CensusLineRow.AddNew }
            .Concat(tree.People.Values.OrderBy(p => p.PrimaryName.SortKey)
                .Select(p => new PersonChoice(p.Id, p.Lifespan.Length > 0 ? $"{p.DisplayName} ({p.Lifespan})" : p.DisplayName)))
            .ToList();
        SourceTitles = tree.Sources.Values.Select(s => s.Title).Where(t => t.Length > 0).Distinct().OrderBy(t => t).ToList();
        var first = principal != null ? _people.First(p => p.Id == principal.Id) : CensusLineRow.AddNew;
        Rows.Add(new RecordLineRow(this, RecordTranscription.PrincipalRole(RecordKind.Baptism), first, principal?.DisplayName ?? ""));
    }

    public IReadOnlyList<PersonChoice> People => _people;
    public IReadOnlyList<string> SourceTitles { get; }
    public ObservableCollection<RecordLineRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind), nameof(Roles), nameof(DateLabel), nameof(SecondDateLabel), nameof(HasSecondDate), nameof(Title))]
    private int _kindIndex;
    [ObservableProperty] private string _sourceTitle = "";
    [ObservableProperty] private string _reference = "";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _secondDate = "";
    [ObservableProperty] private string _place = "";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _addDetails = true;

    public RecordKind Kind => (RecordKind)KindIndex;
    public string Title => $"Transcribe a {RecordTranscription.Label(Kind).ToLowerInvariant()}";
    public IReadOnlyList<string> Roles => RecordTranscription.Roles(Kind);
    public string DateLabel => RecordTranscription.DateLabel(Kind);
    public string SecondDateLabel => RecordTranscription.SecondDateLabel(Kind);
    public bool HasSecondDate => SecondDateLabel.Length > 0;
    public string DateHint => Display.DateHint(Date);
    public bool HasDateHint => DateHint.Length > 0;

    partial void OnDateChanged(string value)
    {
        OnPropertyChanged(nameof(DateHint));
        OnPropertyChanged(nameof(HasDateHint));
    }

    /// <summary>A new kind of record: the first person becomes its principal, and roles it doesn't have are reset.</summary>
    partial void OnKindIndexChanged(int value)
    {
        var roles = Roles;
        for (var i = 0; i < Rows.Count; i++)
            if (i == 0) Rows[i].Role = roles[0];
            else if (!roles.Contains(Rows[i].Role)) Rows[i].Role = roles.Count > 1 ? roles[1] : roles[0];
        if (!HasSecondDate) SecondDate = "";
    }

    internal string NameOf(long id) => _tree.Person(id)?.DisplayName ?? "";

    [RelayCommand]
    private void AddLine()
    {
        var used = Rows.Select(r => r.Role).ToHashSet();
        var role = Roles.FirstOrDefault(r => !used.Contains(r)) ?? Roles[^1];
        Rows.Add(new RecordLineRow(this, role, CensusLineRow.NotInTree, ""));
    }

    [RelayCommand]
    private void RemoveLine(RecordLineRow row)
    {
        if (Rows.Count > 1) Rows.Remove(row);
    }

    public Transcription ToTranscription()
    {
        var t = new Transcription
        {
            Kind = Kind, SourceTitle = SourceTitle, Reference = Reference, Date = Date, SecondDate = SecondDate,
            Place = Place, Text = Text, AddDetails = AddDetails,
        };
        t.Lines.AddRange(Rows.Select(r => r.ToLine()));
        return t;
    }
}
