using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;

namespace Lineage.App.ViewModels;

/// <summary>One line in the quick search: a person, place, source or command, and what choosing it does.</summary>
public sealed record QuickResult(string Kind, string Title, string Detail, Action Run)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>
/// Quick search (Ctrl+K): type to jump to any person, place or source, or to run a command, without the mouse.
/// People match on every word of any of their names (or a year); commands match on their names.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxPeopleResults = 8, MaxOtherResults = 4;

    public ObservableCollection<QuickResult> QuickResults { get; } = new();
    [ObservableProperty] private bool _isQuickSearchOpen;
    [ObservableProperty] private string _quickSearchText = "";
    [ObservableProperty] private QuickResult? _selectedQuickResult;

    partial void OnQuickSearchTextChanged(string value) => RebuildQuickResults();

    [RelayCommand]
    private void OpenQuickSearch()
    {
        QuickSearchText = "";
        RebuildQuickResults();
        IsQuickSearchOpen = true;
    }

    [RelayCommand] private void CloseQuickSearch() => IsQuickSearchOpen = false;

    /// <summary>Runs the chosen result (or the highlighted one) and closes the search.</summary>
    [RelayCommand]
    private void RunQuickResult(QuickResult? result)
    {
        var r = result ?? SelectedQuickResult ?? QuickResults.FirstOrDefault();
        if (r == null) return;
        IsQuickSearchOpen = false;
        r.Run();
    }

    /// <summary>Moves the highlight up or down the results (wrapping around).</summary>
    public void MoveQuickSelection(int delta)
    {
        if (QuickResults.Count == 0) return;
        var i = SelectedQuickResult == null ? (delta > 0 ? -1 : 0) : QuickResults.IndexOf(SelectedQuickResult);
        SelectedQuickResult = QuickResults[((i + delta) % QuickResults.Count + QuickResults.Count) % QuickResults.Count];
    }

    private void RebuildQuickResults()
    {
        QuickResults.Clear();
        var text = QuickSearchText.Trim();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (HasTree)
        {
            var people = words.Length == 0
                ? _tree.Bookmarks.Select(id => _tree.Person(id)).OfType<Lineage.Core.Model.Person>()
                : _tree.People.Values.Where(p => words.All(w =>
                      p.Names.Any(n => n.Full.Contains(w, StringComparison.OrdinalIgnoreCase)) || p.Lifespan.Contains(w)))
                  .OrderBy(p => p.PrimaryName.SortKey);
            foreach (var p in people.Take(MaxPeopleResults))
            {
                var id = p.Id;
                QuickResults.Add(new QuickResult("Person", p.DisplayName, p.Lifespan, () => OpenPerson(id)));
            }

            if (words.Length > 0)
            {
                foreach (var place in PlaceIndex.Build(_tree)
                             .Where(pl => words.All(w => pl.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                             .OrderByDescending(pl => pl.EventCount).ThenByDescending(pl => pl.Name.Length).Take(MaxOtherResults))
                {
                    var name = place.Name;
                    QuickResults.Add(new QuickResult("Place", name, $"{place.EventCount:N0} event{(place.EventCount == 1 ? "" : "s")}", () => GoToPlace(name)));
                }
                foreach (var s in _tree.Sources.Values
                             .Where(s => words.All(w => s.Title.Contains(w, StringComparison.OrdinalIgnoreCase)))
                             .OrderBy(s => s.Title).Take(MaxOtherResults))
                {
                    var id = s.Id;
                    QuickResults.Add(new QuickResult("Source", s.Title.Length > 0 ? s.Title : $"Source {s.Id}", s.Author, () => GoToSource(id)));
                }
            }
        }

        foreach (var (label, command, parameter) in QuickCommands())
            if (words.All(w => label.Contains(w, StringComparison.OrdinalIgnoreCase)) && command.CanExecute(parameter))
            {
                if (words.Length == 0 && QuickResults.Count >= MaxPeopleResults + 6) break;
                QuickResults.Add(new QuickResult("Command", label, "", () => command.Execute(parameter)));
            }
        SelectedQuickResult = QuickResults.FirstOrDefault();
    }

    private IEnumerable<(string Label, ICommand Command, object? Parameter)> QuickCommands() => new (string, ICommand, object?)[]
    {
        ("Add a person", AddPersonCommand, null),
        ("Edit this person", EditPersonCommand, null),
        ("Add an event", AddEventCommand, null),
        ("Show details", ShowViewCommand, PersonView.Details),
        ("Show pedigree chart", ShowViewCommand, PersonView.Pedigree),
        ("Show fan chart", ShowViewCommand, PersonView.FanChart),
        ("Show descendants chart", ShowViewCommand, PersonView.Descendants),
        ("Show hourglass chart", ShowViewCommand, PersonView.Hourglass),
        ("Show family map", ShowViewCommand, PersonView.Map),
        ("Show timeline and census checklist", ShowViewCommand, PersonView.Timeline),
        ("Go to the home person", GoToHomePersonCommand, null),
        ("How is this person related to…", FindRelationshipCommand, null),
        ("DNA matches", ShowDnaMatchesCommand, null),
        ("Find duplicates", FindDuplicatesCommand, null),
        ("Find problems", FindProblemsCommand, null),
        ("New research task", NewResearchCommand, null),
        ("Family history book", FamilyBookCommand, null),
        ("Make a family website", ExportWebsiteCommand, null),
        ("Share a copy with a relative", ShareWithRelativeCommand, null),
        ("Review a relative's changes", ReviewRelativeChangesCommand, null),
        ("Export GEDCOM", ExportGedcomCommand, null),
        ("Import a file", ImportCommand, null),
        ("Open a tree", OpenCommand, null),
        ("Save", SaveCommand, null),
        ("Settings", OpenSettingsCommand, null),
        ("Check for updates", CheckForUpdatesCommand, null),
    };

    private void GoToPlace(string name)
    {
        SearchText = "";
        Section = AppSection.Places;
        SelectedPlace = PlaceList.FirstOrDefault(r => r.Name == name) ?? SelectedPlace;
    }

    private void GoToSource(long id)
    {
        SearchText = "";
        Section = AppSection.Sources;
        SelectedSource = SourceList.FirstOrDefault(r => r.Id == id) ?? SelectedSource;
    }
}
