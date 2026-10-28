using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Places;

namespace Lineage.App.ViewModels;

/// <summary>One place and the gazetteer's suggestion for it.</summary>
public sealed partial class GpsRow : ObservableObject
{
    public GpsRow(GeoSuggestion s)
    {
        Suggestion = s;
        // Certain town matches start ticked; guesses and state centres are for the user to decide.
        _isChecked = s.Match.Sure && s.Match.Precision == GeoPrecision.Town;
    }

    public GeoSuggestion Suggestion { get; }
    public string Place => Suggestion.Place.Name;
    public string Uses => $"{Suggestion.Place.EventCount:N0} event{(Suggestion.Place.EventCount == 1 ? "" : "s")}";
    public string Match => Suggestion.Match.Description;
    public string Coordinates => Suggestion.Match.Point.ToString();
    public string Confidence => Suggestion.Match.Precision == GeoPrecision.State ? "State only" : Suggestion.Match.Sure ? "Sure" : "Likely";
    public bool IsSure => Suggestion.Match.Sure;
    [ObservableProperty] private bool _isChecked;
}

/// <summary>Reviews GPS found for places from the built-in gazetteer before it's added.</summary>
public sealed partial class FindGpsViewModel : ObservableObject
{
    public FindGpsViewModel(IReadOnlyList<GeoSuggestion> suggestions, int withoutGps)
    {
        foreach (var s in suggestions.OrderByDescending(s => s.Match.Sure).ThenByDescending(s => s.Place.EventCount))
        {
            var row = new GpsRow(s);
            row.PropertyChanged += (_, _) => Update();
            Rows.Add(row);
        }
        var sure = suggestions.Count(s => s.Match.Sure);
        Summary = suggestions.Count == 0
            ? $"None of the {withoutGps:N0} places without GPS could be found. Places need their country (or US state) in the name to be found."
            : $"Found {suggestions.Count:N0} of the {withoutGps:N0} places without GPS: {sure:N0} sure, the rest likely or only to the state. " +
              "Sure ones are ticked; check the others before ticking them.";
        Update();
    }

    public ObservableCollection<GpsRow> Rows { get; } = new();
    public string Summary { get; }
    [ObservableProperty] private string _applyText = "";

    public IReadOnlyList<GeoSuggestion> Chosen => Rows.Where(r => r.IsChecked).Select(r => r.Suggestion).ToList();

    private void Update()
    {
        var n = Rows.Count(r => r.IsChecked);
        ApplyText = n == 1 ? "Add GPS to 1 place" : $"Add GPS to {n:N0} places";
    }

    [RelayCommand]
    private void TickAll()
    {
        foreach (var r in Rows) r.IsChecked = true;
    }

    [RelayCommand]
    private void UntickAll()
    {
        foreach (var r in Rows) r.IsChecked = false;
    }
}
