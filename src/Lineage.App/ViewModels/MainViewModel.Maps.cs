using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // ---- through time ----

    private IReadOnlyList<MapPin> _allPersonPins = Array.Empty<MapPin>();
    private IReadOnlyList<MapLine> _allPersonLines = Array.Empty<MapLine>();
    private DispatcherTimer? _mapTimer;

    /// <summary>The year the person map shows (everything up to and including it). A double for the slider.</summary>
    [ObservableProperty] private double _mapYear;
    [ObservableProperty] private int _mapYearMin;
    [ObservableProperty] private int _mapYearMax;
    [ObservableProperty] private bool _hasMapTimeline;
    [ObservableProperty] private bool _isMapPlaying;
    [ObservableProperty] private string _mapYearText = "";
    [ObservableProperty] private string _mapLatestText = "";
    /// <summary>Fit the map to the pins when they change: on for a new person, off while moving through the years.</summary>
    [ObservableProperty] private bool _mapAutoFit = true;

    public string MapPlayText => IsMapPlaying ? "Pause" : MapYear >= MapYearMax ? "Play from the start" : "Play";
    partial void OnIsMapPlayingChanged(bool value) => OnPropertyChanged(nameof(MapPlayText));

    private bool _resettingMapYear;

    partial void OnMapYearChanged(double value)
    {
        if (_resettingMapYear) return;
        MapAutoFit = false;   // keep the view still while the years change
        ApplyMapYear();
        OnPropertyChanged(nameof(MapPlayText));
    }

    /// <summary>The person, their spouses and children, and their ancestors, with arrows from each parent's birthplace.</summary>
    private void RebuildPersonMap()
    {
        StopMapPlayback();
        if (_current is not { } p)
        {
            _allPersonPins = Array.Empty<MapPin>();
            _allPersonLines = Array.Empty<MapLine>();
            PersonPins = Array.Empty<MapPin>();
            PersonLines = Array.Empty<MapLine>();
            HasMapTimeline = false;
            return;
        }
        var people = _tree.Ancestors(p, 6).Values.ToList();
        foreach (var (_, spouse, children) in _tree.FamiliesOf(p))
        {
            if (spouse != null) people.Add(spouse);
            people.AddRange(children);
        }
        var (pins, lines) = MapData.For(_tree, people.Distinct());
        _allPersonPins = pins;
        _allPersonLines = lines;
        var years = pins.Select(x => x.Year).Concat(lines.Select(x => x.Year)).Where(y => y > 0).ToList();
        HasMapTimeline = years.Distinct().Count() >= 2;
        MapYearMin = years.Count > 0 ? years.Min() : 0;
        MapYearMax = years.Count > 0 ? years.Max() : 0;

        // Show everything, fitted, then let the slider take over.
        MapAutoFit = true;
        PersonPins = pins;
        PersonLines = lines;
        _resettingMapYear = true;
        MapYear = MapYearMax;
        _resettingMapYear = false;
        OnPropertyChanged(nameof(MapPlayText));
        UpdateMapYearText();
        PersonMapText = pins.Count == 0
            ? "No GPS locations yet for this family. Add them on the Places page, in an event, or on this person's Places card — then they appear here."
            : $"{pins.Count:N0} places for {p.DisplayName}, their family and their ancestors. Arrows show where each generation moved."
              + (HasMapTimeline ? " Drag the slider or press Play to watch the family move over time." : "");
    }

    /// <summary>Shows pins and arrows up to the chosen year; undated ones are always shown.</summary>
    private void ApplyMapYear()
    {
        var year = (int)Math.Round(MapYear);
        PersonPins = _allPersonPins.Where(x => x.Year == 0 || x.Year <= year).ToList();
        PersonLines = _allPersonLines.Where(x => x.Year == 0 || x.Year <= year).ToList();
        UpdateMapYearText();
    }

    private void UpdateMapYearText()
    {
        var year = (int)Math.Round(MapYear);
        MapYearText = HasMapTimeline ? year.ToString() : "";
        var latest = _allPersonPins.Where(x => x.Year > 0 && x.Year <= year).OrderByDescending(x => x.Year).FirstOrDefault();
        MapLatestText = latest == null ? "" : $"{latest.Label}: {latest.Detail}";
    }

    [RelayCommand]
    private void PlayMap()
    {
        if (IsMapPlaying) { StopMapPlayback(); return; }
        if (!HasMapTimeline) return;
        if (MapYear >= MapYearMax) MapYear = MapYearMin;
        // About 15 seconds for the whole span, at least a year per step.
        var step = Math.Max(1.0, (MapYearMax - MapYearMin) / 120.0);
        _mapTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _mapTimer.Tick -= OnMapTick;
        _mapTimer.Tick += OnMapTick;
        _mapStep = step;
        IsMapPlaying = true;
        _mapTimer.Start();
    }

    private double _mapStep = 1;

    private void OnMapTick(object? sender, EventArgs e) => StepMap(_mapStep);

    /// <summary>Moves the map on by some years; stops at the end. (Public to the tests through the command.)</summary>
    internal void StepMap(double years)
    {
        var next = Math.Min(MapYearMax, MapYear + years);
        MapYear = next;
        if (next >= MapYearMax) StopMapPlayback();
    }

    private void StopMapPlayback()
    {
        _mapTimer?.Stop();
        IsMapPlaying = false;
    }

    /// <summary>One pin per place that has GPS (for the Places page).</summary>
    private void RebuildPlacePins() =>
        PlacePins = _allPlaces.Where(r => r.Entry.Location != null)
            .Select(r => new MapPin(r.Entry.Location!.Value, r.Name, r.Detail.Replace("  ·  ", ", ")))
            .ToList();
}
