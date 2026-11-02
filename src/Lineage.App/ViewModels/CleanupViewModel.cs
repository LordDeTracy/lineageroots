using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One proposed clean-up in the wizard, with its tick box.</summary>
public sealed partial class CleanupRow : ObservableObject
{
    public CleanupRow(CleanupFix fix)
    {
        Fix = fix;
        // A date that could be read two ways is left for the user to tick.
        _isChosen = !fix.Note.StartsWith("Could also be", StringComparison.Ordinal);
    }

    public CleanupFix Fix { get; }
    [ObservableProperty] private bool _isChosen;
    public string Before => Fix.Before;
    public string After => Fix.Kind == CleanupKind.Dates ? Display.Date(GenDate.Parse(Fix.After)) : Fix.After;
    public string Details => $"{Fix.Count:N0} × · {Fix.Where}";
    public string Note => Fix.Note;
    public bool IsUnsure => Fix.Note.StartsWith("Could also be", StringComparison.Ordinal);
}

/// <summary>A heading in the wizard (names, dates, places) with its rows and tick-all/none.</summary>
public sealed partial class CleanupGroup(string title, string explanation) : ObservableObject
{
    public string Title { get; } = title;
    public string Explanation { get; } = explanation;
    public ObservableCollection<CleanupRow> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;
    public string Heading => $"{Title} ({Rows.Count:N0})";

    internal void Refresh()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(Heading));
    }

    [RelayCommand] private void ChooseAll() { foreach (var r in Rows) r.IsChosen = true; }
    [RelayCommand] private void ChooseNone() { foreach (var r in Rows) r.IsChosen = false; }
}

/// <summary>
/// The clean-up wizard: finds names in capitals, dates the program can't read and place names written
/// several ways, shows each change before → after, and hands back the ones the user ticked (applied
/// by the main window as one undoable change).
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly FamilyTree _tree;

    public CleanupViewModel(FamilyTree tree, bool? dayFirst = null)
    {
        _tree = tree;
        _dayFirst = dayFirst ?? CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.TrimStart().StartsWith("d", StringComparison.OrdinalIgnoreCase);
        Groups = new[] { Names, Dates, Places };
        Scan();
    }

    public CleanupGroup Names { get; } = new("Names", "Names typed in CAPITALS or all small letters, or with extra spaces.");
    public CleanupGroup Dates { get; } = new("Dates", "Dates written in a form the program can't read, so they don't sort, check or show on timelines.");
    public CleanupGroup Places { get; } = new("Places", "The same place written different ways, and place names with untidy commas or spaces.");
    public IReadOnlyList<CleanupGroup> Groups { get; }

    /// <summary>How to read dates like 03/04/1850: day first (UK, Europe) or month first (US).</summary>
    [ObservableProperty] private bool _dayFirst;
    public bool MonthFirst { get => !DayFirst; set => DayFirst = !value; }
    partial void OnDayFirstChanged(bool value)
    {
        OnPropertyChanged(nameof(MonthFirst));
        Scan();
    }

    [ObservableProperty] private string _summary = "";
    public bool IsClean => Groups.All(g => !g.HasRows);

    public IEnumerable<CleanupFix> Chosen => Groups.SelectMany(g => g.Rows).Where(r => r.IsChosen).Select(r => r.Fix);

    private void Scan()
    {
        var fixes = Cleanup.Find(_tree, DayFirst);
        foreach (var g in Groups)
        {
            foreach (var r in g.Rows) r.PropertyChanged -= OnRowChanged;
            g.Rows.Clear();
        }
        foreach (var f in fixes)
        {
            var row = new CleanupRow(f);
            row.PropertyChanged += OnRowChanged;
            (f.Kind switch { CleanupKind.Names => Names, CleanupKind.Dates => Dates, _ => Places }).Rows.Add(row);
        }
        foreach (var g in Groups) g.Refresh();
        OnPropertyChanged(nameof(IsClean));
        UpdateSummary();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupRow.IsChosen)) UpdateSummary();
    }

    private void UpdateSummary()
    {
        var rows = Groups.SelectMany(g => g.Rows).ToList();
        var chosen = rows.Count(r => r.IsChosen);
        Summary = rows.Count == 0 ? "Nothing to clean up — names, dates and places all look tidy."
            : $"{chosen:N0} of {rows.Count:N0} changes ticked. Nothing changes until you press Clean up, and Ctrl+Z undoes it all.";
        HasChosen = chosen > 0;
    }

    [ObservableProperty] private bool _hasChosen;
}
