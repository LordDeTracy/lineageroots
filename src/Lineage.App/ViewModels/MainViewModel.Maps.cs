using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.App.Controls;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>Maps: where the current person and their ancestors lived, and every place in the tree.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private IReadOnlyList<MapPin> _personPins = Array.Empty<MapPin>();
    [ObservableProperty] private IReadOnlyList<MapLine> _personLines = Array.Empty<MapLine>();
    [ObservableProperty] private string _personMapText = "";
    [ObservableProperty] private IReadOnlyList<MapPin> _placePins = Array.Empty<MapPin>();
    public bool HasPersonPins => PersonPins.Count > 0;
    public bool HasPlacePins => PlacePins.Count > 0;
    partial void OnPersonPinsChanged(IReadOnlyList<MapPin> value) => OnPropertyChanged(nameof(HasPersonPins));
    partial void OnPlacePinsChanged(IReadOnlyList<MapPin> value) => OnPropertyChanged(nameof(HasPlacePins));

    /// <summary>The person, their spouses and children, and their ancestors, with arrows from each parent's birthplace.</summary>
    private void RebuildPersonMap()
    {
        if (_current is not { } p) { PersonPins = Array.Empty<MapPin>(); PersonLines = Array.Empty<MapLine>(); return; }
        var people = _tree.Ancestors(p, 6).Values.ToList();
        foreach (var (_, spouse, children) in _tree.FamiliesOf(p))
        {
            if (spouse != null) people.Add(spouse);
            people.AddRange(children);
        }
        var (pins, lines) = MapData.For(_tree, people.Distinct());
        PersonPins = pins;
        PersonLines = lines;
        PersonMapText = pins.Count == 0
            ? "No GPS locations yet for this family. Add them on the Places page, in an event, or on this person's Places card — then they appear here."
            : $"{pins.Count:N0} places for {p.DisplayName}, their family and their ancestors. Arrows show where each generation moved.";
    }

    /// <summary>One pin per place that has GPS (for the Places page).</summary>
    private void RebuildPlacePins() =>
        PlacePins = _allPlaces.Where(r => r.Entry.Location != null)
            .Select(r => new MapPin(r.Entry.Location!.Value, r.Name, r.Detail.Replace("  ·  ", ", ")))
            .ToList();
}
