using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed record PersonChoice(long Id, string Text)
{
    public override string ToString() => Text;
}

public sealed record RecordOption(long Id, string Text)
{
    public static readonly RecordOption None = new(0, "None");
    public override string ToString() => Text;
}

/// <summary>
/// Edits one research task ("Find the 1881 census for Thomas"): what to do, priority, where to look,
/// and which people it's about. Works on copies; <see cref="Apply"/> writes back.
/// </summary>
public sealed partial class ResearchEditorViewModel : ObservableObject
{
    public static readonly string[] Priorities = { "None", "High", "Medium", "Low" };
    /// <summary>A task still to do, or a logged search and what came of it (index = <see cref="SearchResult"/>).</summary>
    public static readonly string[] Results = { "To do", "Searched: found", "Searched: not found", "Searched: unclear" };
    private readonly string _hintKey;

    private readonly FamilyTree _tree;

    public ResearchEditorViewModel(FamilyTree tree, ResearchItem item, bool isNew, IEnumerable<long> linkedPeople)
    {
        _tree = tree;
        IsNew = isNew;
        Title = isNew ? "New research task" : "Edit research task";
        _description = item.Description;
        _category = item.Category;
        _priorityIndex = Math.Clamp(item.Priority, 0, 3);
        _resultIndex = (int)item.Result;
        _hintKey = item.HintKey;
        _done = item.Done;
        _date = Display.Date(item.Date);
        _note = item.Note;
        Repositories = new[] { RecordOption.None }
            .Concat(tree.Repositories.Values.OrderBy(r => r.Name).Select(r => new RecordOption(r.Id, r.Name.Length > 0 ? r.Name : $"Repository {r.Id}"))).ToList();
        Sources = new[] { RecordOption.None }
            .Concat(tree.Sources.Values.OrderBy(s => s.Title).Select(s => new RecordOption(s.Id, s.Title.Length > 0 ? s.Title : $"Source {s.Id}"))).ToList();
        _repository = Repositories.FirstOrDefault(r => r.Id == item.RepositoryId) ?? RecordOption.None;
        _source = Sources.FirstOrDefault(s => s.Id == item.SourceId) ?? RecordOption.None;
        foreach (var id in linkedPeople)
            if (tree.Person(id) is { } p) People.Add(Option(p));
        Categories = tree.Research.Values.Select(r => r.Category).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order()
            .Concat(new[] { "Census", "Parish register", "Civil registration", "Probate", "Newspaper", "Military", "Immigration", "Cemetery" })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AllPeople = tree.People.Values.OrderBy(p => p.PrimaryName.SortKey).Select(Option).ToList();
    }

    private static PersonChoice Option(Person p) =>
        new(p.Id, p.Lifespan.Length > 0 ? $"{p.DisplayName} ({p.Lifespan})" : p.DisplayName);

    public string Title { get; }
    public bool IsNew { get; }
    public bool CanDelete => !IsNew;
    public string[] PriorityOptions => Priorities;
    public IReadOnlyList<RecordOption> Repositories { get; }
    public IReadOnlyList<RecordOption> Sources { get; }
    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<PersonChoice> AllPeople { get; }
    public ObservableCollection<PersonChoice> People { get; } = new();

    [ObservableProperty] private string _description;
    [ObservableProperty] private string _category;
    [ObservableProperty] private int _priorityIndex;
    [ObservableProperty] private int _resultIndex;
    public string[] ResultOptions => Results;
    [ObservableProperty] private bool _done;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DateHint), nameof(HasDateHint))] private string _date;
    [ObservableProperty] private string _note;
    [ObservableProperty] private RecordOption _repository;
    [ObservableProperty] private RecordOption _source;
    [ObservableProperty] private PersonChoice? _personToAdd;

    public string DateHint => Display.DateHint(Date);
    public bool HasDateHint => DateHint.Length > 0;
    public bool IsValid => Description.Trim().Length > 0;

    partial void OnPersonToAddChanged(PersonChoice? value)
    {
        if (value == null) return;
        if (People.All(p => p.Id != value.Id)) People.Add(value);
        PersonToAdd = null;
    }

    [RelayCommand] private void RemovePerson(PersonChoice p) => People.Remove(p);

    public IReadOnlyList<long> PersonIds => People.Select(p => p.Id).ToList();

    public void Apply(ResearchItem item)
    {
        item.Description = Description.Trim();
        item.Category = Category.Trim();
        item.Priority = PriorityIndex;
        item.Result = (SearchResult)Math.Clamp(ResultIndex, 0, 3);
        item.HintKey = _hintKey;
        item.Done = Done || item.Result != SearchResult.None;   // a logged search is finished by definition
        item.Date = GenDate.Parse(Date);
        item.Note = Note;
        item.RepositoryId = Repository.Id > 0 ? Repository.Id : null;
        item.SourceId = Source.Id > 0 ? Source.Id : null;
    }
}
