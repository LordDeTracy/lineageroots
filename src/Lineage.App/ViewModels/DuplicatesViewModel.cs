using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One line of the side-by-side comparison.</summary>
public sealed record CompareLine(string Label, string Left, string Right)
{
    public bool Differs => !string.Equals(Left, Right, StringComparison.OrdinalIgnoreCase) && Left.Length > 0 && Right.Length > 0;
}

public sealed class DuplicatePair
{
    public DuplicatePair(DuplicateMatch m)
    {
        Match = m;
        Title = $"{m.A.DisplayName}  ·  {m.B.DisplayName}";
        Subtitle = $"{m.Score}% match";
    }

    public DuplicateMatch Match { get; }
    public string Title { get; }
    public string Subtitle { get; }
}

/// <summary>
/// Reviews likely duplicates one pair at a time. Merging and dismissing go through callbacks
/// to the main window, so they are undoable like any other edit.
/// </summary>
public sealed partial class DuplicatesViewModel : ObservableObject
{
    private readonly FamilyTree _tree;
    private readonly Action<Person, Person> _merge;
    private readonly Action<Person, Person> _dismiss;

    public DuplicatesViewModel(FamilyTree tree, IEnumerable<DuplicateMatch> matches,
                               Action<Person, Person> merge, Action<Person, Person> dismiss)
    {
        _tree = tree;
        _merge = merge;
        _dismiss = dismiss;
        foreach (var m in matches) Pairs.Add(new DuplicatePair(m));
        _selected = Pairs.FirstOrDefault();
        Refresh();
    }

    public ObservableCollection<DuplicatePair> Pairs { get; } = new();
    public ObservableCollection<CompareLine> Lines { get; } = new();

    [ObservableProperty] private DuplicatePair? _selected;
    [ObservableProperty] private string _reasons = "";
    [ObservableProperty] private string _leftTitle = "";
    [ObservableProperty] private string _rightTitle = "";
    /// <summary>0 keeps the left record (the right one is merged into it), 1 keeps the right.</summary>
    [ObservableProperty] private int _keepIndex;
    [ObservableProperty] private string _summary = "";
    public bool HasSelection => Selected != null;
    public bool IsEmpty => Pairs.Count == 0;

    partial void OnSelectedChanged(DuplicatePair? value) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsEmpty));
        Summary = Pairs.Count == 0 ? "No likely duplicates found." : $"{Pairs.Count} possible duplicate{(Pairs.Count == 1 ? "" : "s")}";
        Lines.Clear();
        if (Selected?.Match is not { } m) { Reasons = LeftTitle = RightTitle = ""; return; }
        Reasons = string.Join("  ·  ", m.Reasons);
        LeftTitle = $"ID {m.A.Id}";
        RightTitle = $"ID {m.B.Id}";
        // The record with more information is the better one to keep.
        KeepIndex = Weight(m.B) > Weight(m.A) ? 1 : 0;
        Add("Name", m.A.DisplayName, m.B.DisplayName);
        Add("Other names", string.Join(", ", m.A.Names.Skip(1).Select(n => n.Full)), string.Join(", ", m.B.Names.Skip(1).Select(n => n.Full)));
        Add("Sex", m.A.Sex.ToString(), m.B.Sex.ToString());
        Add("Birth", Fact(m.A.Birth), Fact(m.B.Birth));
        Add("Death", Fact(m.A.Death), Fact(m.B.Death));
        Add("Parents", Parents(m.A), Parents(m.B));
        Add("Spouses", Spouses(m.A), Spouses(m.B));
        Add("Children", Children(m.A), Children(m.B));
        Add("Events", m.A.Events.Count.ToString(), m.B.Events.Count.ToString());
        Add("Sources", Citations(m.A).ToString(), Citations(m.B).ToString());
        Add("Photos", m.A.Media.Count.ToString(), m.B.Media.Count.ToString());
        Add("FamilySearch", m.A.FamilySearchId, m.B.FamilySearchId);
    }

    private void Add(string label, string left, string right)
    {
        if (left.Length > 0 || right.Length > 0) Lines.Add(new CompareLine(label, left, right));
    }

    private static string Fact(Event? e) => e == null ? "" : Display.Join(Display.Date(e.Date), e.Place);
    private string Parents(Person p) => string.Join(" & ", _tree.ParentsOf(p).Select(x => x.DisplayName));
    private string Spouses(Person p) => string.Join(", ", _tree.FamiliesOf(p).Select(f => f.Spouse?.DisplayName).OfType<string>());
    private string Children(Person p) => _tree.FamiliesOf(p).Sum(f => f.Children.Count) is var n and > 0 ? n.ToString() : "";
    private static int Citations(Person p) => p.Citations.Count + p.Events.Sum(e => e.Citations.Count);
    private static int Weight(Person p) => p.Events.Count * 3 + Citations(p) * 2 + p.Media.Count + p.SpouseFamilyIds.Count + p.ParentFamilyIds.Count;

    [RelayCommand]
    private void Merge()
    {
        if (Selected?.Match is not { } m) return;
        var (keep, other) = KeepIndex == 0 ? (m.A, m.B) : (m.B, m.A);
        _merge(keep, other);
        // Any other pair that involved the merged-away record is now stale.
        RemoveWhere(p => p.Match.A == other || p.Match.B == other || p == Selected);
    }

    [RelayCommand]
    private void NotDuplicate()
    {
        if (Selected?.Match is not { } m) return;
        _dismiss(m.A, m.B);
        RemoveWhere(p => p == Selected);
    }

    [RelayCommand]
    private void Skip()
    {
        if (Selected == null) return;
        var i = Pairs.IndexOf(Selected);
        Selected = Pairs.Count > 0 ? Pairs[(i + 1) % Pairs.Count] : null;
    }

    private void RemoveWhere(Func<DuplicatePair, bool> gone)
    {
        var i = Selected != null ? Pairs.IndexOf(Selected) : 0;
        foreach (var p in Pairs.Where(gone).ToList()) Pairs.Remove(p);
        Selected = Pairs.Count == 0 ? null : Pairs[Math.Min(Math.Max(i, 0), Pairs.Count - 1)];
        Refresh();
    }
}
