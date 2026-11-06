using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.App.ViewModels;

/// <summary>One line in a side-by-side column: a label ("Birth", "Father") and what it says.</summary>
public sealed record SideLine(string Label, string Text, bool Heading = false);

/// <summary>One person's column in the side-by-side view.</summary>
public sealed partial class SideColumn : ObservableObject
{
    private readonly FamilyTree _tree;
    public SideColumn(FamilyTree tree) => _tree = tree;

    public ObservableCollection<SideLine> Lines { get; } = new();
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _years = "";
    public long PersonId { get; private set; }

    public void Show(Person? p)
    {
        Lines.Clear();
        PersonId = p?.Id ?? 0;
        Name = p?.DisplayName ?? "Choose someone";
        Years = p?.Lifespan ?? "";
        if (p == null) return;
        foreach (var n in p.Names.Skip(1).Where(n => n.Full.Length > 0)) Lines.Add(new SideLine("Also known as", n.Full));
        var facts = p.Events.Select((e, i) => (e, i)).OrderBy(x => x.e.Date.SortKey == 0 ? int.MaxValue : x.e.Date.SortKey).ThenBy(x => x.i).Select(x => x.e);
        foreach (var e in facts)
            Lines.Add(new SideLine(EventTypes.Label(e), Display.Join(Display.Date(e.Date), e.Place.Trim(), StoryWriterValue(e))));
        Lines.Add(new SideLine("Family", "", Heading: true));
        foreach (var parent in _tree.ParentsOf(p).Distinct())
            Lines.Add(new SideLine(parent.Sex == Sex.Female ? "Mother" : parent.Sex == Sex.Male ? "Father" : "Parent", Display.Join(parent.DisplayName, parent.Lifespan)));
        foreach (var (family, spouse, children) in _tree.FamiliesOf(p))
        {
            if (spouse != null) Lines.Add(new SideLine("Spouse", Display.Join(spouse.DisplayName, spouse.Lifespan,
                family.Marriage is { } m ? Display.Join(Display.Date(m.Date), m.Place.Trim()) : "")));
            foreach (var c in children) Lines.Add(new SideLine("Child", Display.Join(c.DisplayName, c.Lifespan)));
        }
        if (p.Note.Trim().Length > 0)
        {
            Lines.Add(new SideLine("Notes", "", Heading: true));
            Lines.Add(new SideLine("", p.Note.Trim().Length > 600 ? p.Note.Trim()[..600] + "…" : p.Note.Trim()));
        }
    }

    private static string StoryWriterValue(Event e)
    {
        var v = e.Value.Trim();
        return v is "Y" or "y" ? "" : v;
    }
}

/// <summary>
/// Two people side by side: the one showing and anyone chosen, with their facts, family and notes in two columns.
/// For comparing a possible duplicate, two candidates for the same record, or a person and a relative.
/// </summary>
public sealed partial class SideBySideViewModel : ObservableObject
{
    private readonly FamilyTree _tree;
    private readonly Dictionary<string, Person> _people;

    public SideBySideViewModel(FamilyTree tree, Person left, Person? right = null)
    {
        _tree = tree;
        _people = tree.People.Values.GroupBy(EventEditorViewModel.PersonLabel).ToDictionary(g => g.Key, g => g.First());
        PeopleNames = _people.Keys.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        Left = new SideColumn(tree);
        Right = new SideColumn(tree);
        Left.Show(left);
        Right.Show(right);
        _rightName = right == null ? "" : EventEditorViewModel.PersonLabel(right);
    }

    public SideColumn Left { get; }
    public SideColumn Right { get; }
    public IReadOnlyList<string> PeopleNames { get; }
    /// <summary>The person to open in the main window when this one closes, if the user asked.</summary>
    public long? OpenPersonId { get; private set; }

    [ObservableProperty] private string _rightName;
    partial void OnRightNameChanged(string value)
    {
        if (_people.TryGetValue(value.Trim(), out var p)) Right.Show(p);
    }

    [RelayCommand]
    private void Swap()
    {
        var (l, r) = (Left.PersonId, Right.PersonId);
        if (r == 0) return;
        Left.Show(_tree.Person(r));
        Right.Show(_tree.Person(l));
        RightName = _tree.Person(l) is { } p ? EventEditorViewModel.PersonLabel(p) : "";
    }

    /// <summary>Asks the window to close and the main window to show this column's person.</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    private void Open(SideColumn column)
    {
        if (column.PersonId == 0) return;
        OpenPersonId = column.PersonId;
        CloseRequested?.Invoke();
    }
}
