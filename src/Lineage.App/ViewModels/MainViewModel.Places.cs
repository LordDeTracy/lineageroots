using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>A place in the Places list.</summary>
public sealed record PlaceListRow(PlaceEntry Entry)
{
    public string Name => Entry.Name;
    public string Detail => Display.Join($"{Entry.EventCount:N0} event{(Entry.EventCount == 1 ? "" : "s")}",
        Entry.FirstYear > 0 ? Entry.FirstYear == Entry.LastYear ? $"{Entry.FirstYear}" : $"{Entry.FirstYear}–{Entry.LastYear}" : "");
    public bool HasGps => Entry.Location != null;
    public override string ToString() => Name;
}

/// <summary>Something that happened at the open place.</summary>
public sealed record PlaceEventRow(long PersonId, string Name, string What, string Date);

/// <summary>Place-name clean-up suggestion shown on the Places page.</summary>
public sealed record PlaceSuggestionRow(PlaceSuggestion Suggestion)
{
    public string Standard => Suggestion.Standard;
    public string Variants => string.Join("  ·  ", Suggestion.Variants);
    public string Reason => Suggestion.Reason;
}

/// <summary>The Places section: every place in the tree, GPS for each, and tidying up spellings.</summary>
public sealed partial class MainViewModel
{
    private List<PlaceListRow> _allPlaces = new();

    public ObservableCollection<PlaceListRow> PlaceList { get; } = new();
    public ObservableCollection<PlaceEventRow> PlaceEvents { get; } = new();
    public ObservableCollection<PlaceSuggestionRow> PlaceSuggestions { get; } = new();

    [ObservableProperty] private PlaceListRow? _selectedPlace;
    [ObservableProperty] private string _placeCountText = "";
    [ObservableProperty] private string _placeTitle = "";
    [ObservableProperty] private string _placeSummary = "";
    [ObservableProperty] private string _placeCoordinates = "";
    [ObservableProperty] private string _placeCoordinatesError = "";
    [ObservableProperty] private bool _hasPlaceSuggestions;
    public bool HasSelectedPlace => SelectedPlace != null;
    public bool HasNoPlaces => _allPlaces.Count == 0;
    public bool HasPlaceCoordinatesError => PlaceCoordinatesError.Length > 0;
    public string PlaceMapUrl => GeoPoint.Parse(PlaceCoordinates) is { } at ? at.OpenStreetMapUrl
        : "https://www.openstreetmap.org/search?query=" + Uri.EscapeDataString(PlaceTitle);

    partial void OnSelectedPlaceChanged(PlaceListRow? value) => ShowPlace();
    partial void OnPlaceCoordinatesErrorChanged(string value) => OnPropertyChanged(nameof(HasPlaceCoordinatesError));
    partial void OnPlaceCoordinatesChanged(string value)
    {
        OnPropertyChanged(nameof(PlaceMapUrl));
        if (_showingPlace || SelectedPlace is not { } row) return;
        GeoPoint? at = null;
        if (value.Trim().Length > 0 && (at = GeoPoint.Parse(value)) == null)
        {
            PlaceCoordinatesError = "Couldn't read that — try something like 42.2626, -71.8023";
            return;
        }
        PlaceCoordinatesError = "";
        if (row.Entry.Location == at) return;
        var name = row.Name;
        Change(at == null ? "clear GPS" : "set GPS", PlaceIndex.Touched(_tree, new[] { name }),
               () => PlaceIndex.SetLocation(_tree, name, at));
    }

    private bool _showingPlace;

    /// <summary>Finds GPS for places that have none, from the built-in gazetteer, and adds the ones the user ticks.</summary>
    [RelayCommand]
    private async Task FindGpsAsync()
    {
        if (!HasTree) return;
        Status = "Looking up places…";
        var tree = _tree;
        var (suggestions, without) = await Task.Run(() =>
            (Lineage.Core.Places.Gazetteer.Default.Suggest(tree), Lineage.Core.Analysis.PlaceIndex.Build(tree).Count(p => p.Location == null)));
        Status = "";
        var review = new FindGpsViewModel(suggestions, without);
        if (!await _dialogs.FindGpsAsync(review)) return;
        var chosen = review.Chosen;
        if (chosen.Count == 0) return;
        Change($"add GPS to {chosen.Count} places", Lineage.Core.Analysis.PlaceIndex.Touched(_tree, chosen.Select(c => c.Place.Name)), () =>
        {
            foreach (var c in chosen) Lineage.Core.Analysis.PlaceIndex.SetLocation(_tree, c.Place.Name, c.Match.Point);
        });
        RebuildPlaceList();
        Status = chosen.Count == 1 ? "Added GPS to 1 place" : $"Added GPS to {chosen.Count:N0} places";
    }

    private void RebuildPlaceList()
    {
        _allPlaces = PlaceIndex.Build(_tree).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                               .Select(e => new PlaceListRow(e)).ToList();
        ApplyPlaceFilter();
        PlaceSuggestions.Clear();
        foreach (var s in PlaceIndex.Suggest(_allPlaces.Select(r => r.Entry).ToList()).Take(50))
            PlaceSuggestions.Add(new PlaceSuggestionRow(s));
        HasPlaceSuggestions = PlaceSuggestions.Count > 0;
        OnPropertyChanged(nameof(HasNoPlaces));
        RebuildPlacePins();
    }

    private void ApplyPlaceFilter()
    {
        var keep = SelectedPlace?.Name;
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        PlaceList.Clear();
        foreach (var r in _allPlaces.Where(r => words.All(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))
            PlaceList.Add(r);
        var located = _allPlaces.Count(r => r.HasGps);
        PlaceCountText = PlaceList.Count == _allPlaces.Count
            ? $"{_allPlaces.Count:N0} PLACES · {located:N0} WITH GPS"
            : $"{PlaceList.Count:N0} of {_allPlaces.Count:N0}";
        SelectedPlace = PlaceList.FirstOrDefault(r => r.Name == keep) ?? PlaceList.FirstOrDefault();
        ShowPlace();
    }

    private void ShowPlace()
    {
        OnPropertyChanged(nameof(HasSelectedPlace));
        PlaceEvents.Clear();
        _showingPlace = true;
        try
        {
            if (SelectedPlace is not { } row)
            {
                PlaceTitle = PlaceSummary = PlaceCoordinates = "";
                return;
            }
            PlaceTitle = row.Name;
            PlaceSummary = Display.Join($"{row.Entry.EventCount:N0} events", $"{row.Entry.PersonCount:N0} people",
                row.Entry.FamilyCount > 0 ? $"{row.Entry.FamilyCount:N0} families" : "",
                row.Entry.FirstYear > 0 ? $"{row.Entry.FirstYear}–{row.Entry.LastYear}" : "");
            PlaceCoordinates = row.Entry.Location?.ToString() ?? "";
            PlaceCoordinatesError = "";

            bool At(Event e) => string.Equals(e.Place.Trim(), row.Name, StringComparison.Ordinal)
                             || string.Equals(e.Place2.Trim(), row.Name, StringComparison.Ordinal);
            var rows = new List<(int Sort, PlaceEventRow Row)>();
            foreach (var p in _tree.People.Values)
                foreach (var e in p.Events.Where(At))
                    rows.Add((e.Date.SortKey, new PlaceEventRow(p.Id, p.DisplayName, EventTypes.Label(e), Display.Date(e.Date))));
            foreach (var f in _tree.Families.Values)
                foreach (var e in f.Events.Where(At))
                {
                    var names = string.Join(" and ", f.PartnerIds.Select(id => _tree.Person(id)?.DisplayName).OfType<string>());
                    if (f.PartnerIds.FirstOrDefault() is var pid && pid != 0)
                        rows.Add((e.Date.SortKey, new PlaceEventRow(pid, names, EventTypes.Label(e), Display.Date(e.Date))));
                }
            foreach (var (_, r) in rows.OrderBy(x => x.Sort == 0 ? int.MaxValue : x.Sort).Take(500)) PlaceEvents.Add(r);
        }
        finally { _showingPlace = false; }
        OnPropertyChanged(nameof(PlaceMapUrl));
    }

    [RelayCommand]
    private async Task RenamePlaceAsync()
    {
        if (SelectedPlace is not { } row) return;
        var to = await _dialogs.PromptAsync("Rename place",
            "Type the new name. Choosing a name that's already used merges the two places, everywhere they appear.",
            row.Name, _allPlaces.Select(p => p.Name).Where(n => n != row.Name).ToList());
        if (to == null || to.Trim().Length == 0 || to.Trim() == row.Name) return;
        var target = to.Trim();
        var count = 0;
        Change($"rename {row.Name}", PlaceIndex.Touched(_tree, new[] { row.Name, target }),
               () => count = PlaceIndex.Rename(_tree, row.Name, target));
        SelectedPlace = PlaceList.FirstOrDefault(p => p.Name == target);
        Status = $"Renamed {count:N0} event{(count == 1 ? "" : "s")} to \"{target}\".";
    }

    [RelayCommand]
    private void ApplyPlaceSuggestion(PlaceSuggestionRow row)
    {
        var count = 0;
        Change($"standardise {row.Standard}", PlaceIndex.Touched(_tree, row.Suggestion.Names),
               () => count = PlaceIndex.ApplySuggestion(_tree, row.Suggestion));
        Status = $"Changed {count:N0} event{(count == 1 ? "" : "s")} to \"{row.Standard}\".";
    }

    [RelayCommand]
    private void ApplyAllPlaceSuggestions()
    {
        var all = PlaceSuggestions.Select(r => r.Suggestion).ToList();
        if (all.Count == 0) return;
        var count = 0;
        Change("standardise place names", PlaceIndex.Touched(_tree, all.SelectMany(s => s.Names)),
               () => { foreach (var s in all) count += PlaceIndex.ApplySuggestion(_tree, s); });
        Status = $"Standardised {all.Count:N0} places ({count:N0} events). Ctrl+Z undoes it.";
    }

    [RelayCommand]
    private void DismissPlaceSuggestion(PlaceSuggestionRow row)
    {
        PlaceSuggestions.Remove(row);
        HasPlaceSuggestions = PlaceSuggestions.Count > 0;
    }
}
