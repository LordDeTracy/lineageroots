using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One line of the life timeline: the person's own event, a family event, or history.</summary>
public sealed record TimelineRow(TimelineEntry Entry)
{
    public string Year => Entry.Year.ToString();
    public string Title => Entry.Title;
    public string When => Entry.When;
    public string Place => Entry.Place;
    public string Detail => Entry.Detail;
    public string Age => Entry.Age;
    public bool HasPlace => Place.Length > 0;
    public bool HasDetail => Detail.Length > 0;
    public bool HasAge => Age.Length > 0;
    public bool IsLife => Entry.Kind == TimelineKind.Life;
    public bool IsFamily => Entry.Kind == TimelineKind.Family;
    public bool IsHistory => Entry.Kind == TimelineKind.History;
    public string KindLabel => Entry.History?.Kind switch
    {
        HistoryKind.War => "War",
        HistoryKind.Epidemic => "Epidemic",
        HistoryKind.Migration => "Migration",
        HistoryKind.Law => "Law",
        HistoryKind.Records => "Records",
        HistoryKind.Economy => "Economy",
        HistoryKind.Disaster => "Disaster",
        HistoryKind.Politics => "Politics",
        _ => "",
    };
    public string Tip => Entry.Kind switch
    {
        TimelineKind.Life => "Click to edit",
        TimelineKind.Family when Entry.Relative != null => $"Click to see {Entry.Relative.DisplayName}'s timeline",
        _ => "",
    };
    public string Spoken => Display.Join(Title, When, Place, Age).Replace("  ·  ", ", ");
}

/// <summary>One census on the checklist.</summary>
public sealed record CensusRow(CensusCheck Check, string Date, bool HasTask)
{
    public string Name => Check.Census.Name;
    public string Reason => Check.Reason;
    /// <summary>What the census records — shown unless it's already the reason (for a lost census).</summary>
    public string Note => IsLost ? "" : Check.Census.Note;
    public bool HasNote => Note.Length > 0;
    public bool IsFound => Check.Status == CensusStatus.Found;
    public bool IsToFind => Check.Status == CensusStatus.ToFind;
    public bool IsPossible => Check.Status == CensusStatus.Possible;
    public bool IsLost => Check.Status == CensusStatus.Lost;
    public string StatusText => Check.Status switch
    {
        CensusStatus.Found => "Found",
        CensusStatus.ToFind => "To find",
        CensusStatus.Possible => "Maybe",
        _ => "Lost",
    };
    public bool CanRecord => !IsFound && !IsLost;
    public bool CanAddTask => (IsToFind || IsPossible) && !HasTask;
    public string Spoken => $"{Name}, {StatusText}. {Reason}";
}

/// <summary>The Timeline view: a person's life alongside their family and history, and their census checklist.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<TimelineRow> TimelineRows { get; } = new();
    public ObservableCollection<CensusRow> CensusRows { get; } = new();

    [ObservableProperty] private bool _showFamilyOnTimeline = true;
    [ObservableProperty] private bool _showHistoryOnTimeline = true;
    [ObservableProperty] private string _timelineText = "";
    [ObservableProperty] private string _censusSummary = "";
    [ObservableProperty] private string _censusEmptyText = "";
    [ObservableProperty] private bool _hasCensusRows;
    [ObservableProperty] private bool _hasTimelineRows;

    partial void OnShowFamilyOnTimelineChanged(bool value) => RebuildTimeline();
    partial void OnShowHistoryOnTimelineChanged(bool value) => RebuildTimeline();

    private void RebuildTimeline()
    {
        TimelineRows.Clear();
        CensusRows.Clear();
        if (_current is not { } p)
        {
            HasTimelineRows = HasCensusRows = false;
            return;
        }

        var timeline = LifeTimeline.Build(_tree, p, ShowFamilyOnTimeline, ShowHistoryOnTimeline);
        foreach (var e in timeline.Entries) TimelineRows.Add(new TimelineRow(e));
        HasTimelineRows = TimelineRows.Count > 0;
        var span = timeline.Span;
        var lived = span.Born == 0 ? ""
            : span.DeathKnown ? $"{(span.BornEstimated ? "about " : "")}{span.Born}–{span.Died}"
            : $"born {(span.BornEstimated ? "about " : "")}{span.Born}";
        TimelineText = string.Join(" ", new[]
        {
            HasTimelineRows ? $"{p.DisplayName}'s life{(lived.Length > 0 ? $" ({lived})" : "")}, with their family and what was happening where they lived." : "",
            timeline.Undated > 0 ? $"{timeline.Undated} event{(timeline.Undated == 1 ? " has" : "s have")} no year, so {(timeline.Undated == 1 ? "it isn't" : "they aren't")} shown." : "",
        }.Where(t => t.Length > 0));

        var tasks = PersonTaskDescriptions(p);
        foreach (var check in LifeTimeline.CensusChecklist(_tree, p))
        {
            var c = check.Census;
            var date = c.Month > 0 ? Display.Date(new GenDate { Year = c.TakenYear, Month = c.Month, Day = c.Day }) : c.TakenYear.ToString();
            CensusRows.Add(new CensusRow(check, date, tasks.Contains(CensusTask(p, c))));
        }
        HasCensusRows = CensusRows.Count > 0;
        var found = CensusRows.Count(r => r.IsFound);
        var open = CensusRows.Count(r => !r.IsLost);
        CensusSummary = HasCensusRows ? $"{found} of {open} found" : "";
        CensusEmptyText = span.Born == 0
            ? "Add a birth date (or a marriage or child with a date) to see which censuses they should be in."
            : "No censuses to show. The checklist covers the United States, England, Wales, Scotland, Ireland, Canada, Norway and Sweden — add places to their events so it can tell where they lived.";
    }

    private HashSet<string> PersonTaskDescriptions(Person p) =>
        p.ResearchIds.Select(id => _tree.Research.GetValueOrDefault(id)).OfType<ResearchItem>()
            .Select(r => r.Description).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string CensusTask(Person p, CensusRecord c) => $"Find {p.RecordName} in the {c.Name}";

    [RelayCommand]
    private async Task OpenTimelineEntryAsync(TimelineRow row)
    {
        if (_current is not { } p) return;
        var entry = row.Entry;
        if (entry.Kind == TimelineKind.Life && entry.Event != null && entry.Owner != null)
            await EditEventAsync(ToRow(p, entry.Event, entry.Owner, ""));
        else if (entry.Relative != null)
            Navigate(entry.Relative.Id);
    }

    /// <summary>Adds a census event for the census day, where they probably were, for the user to finish.</summary>
    [RelayCommand]
    private async Task RecordCensusAsync(CensusRow row)
    {
        if (_current is not { } p) return;
        var c = row.Check.Census;
        var e = Event.Of("CENS", new GenDate { Year = c.TakenYear, Month = c.Month, Day = c.Day }, row.Check.Place);
        var editor = new EventEditorViewModel(_tree, e, isNew: true, forFamily: false, $"Record the {c.Name} for {p.DisplayName}");
        editor.AttachPhotos(e, ResolveMedia, () => PickPhotosAsync("Add a picture of the census page"));
        editor.AttachOwner(p);
        if (await _dialogs.EditEventAsync(editor) != EditorResult.Saved) return;
        Change("add census", editor.TouchedSources.Prepend<Entity>(p), () => { editor.Apply(e); p.Events.Add(e); });
    }

    /// <summary>Enters a whole census household for the current person (from a checklist line, or the first still to find).</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task EnterCensusHouseholdAsync(CensusRow? row)
    {
        if (_current is not { } p) return;
        var checklist = LifeTimeline.CensusChecklist(_tree, p);
        var mine = checklist.Where(c => c.Census.Survives).Select(c => c.Census).ToList();
        var all = mine.Concat(Censuses.All.Where(c => c.Survives && !mine.Contains(c))).ToList();
        var initial = row?.Check.Census ?? checklist.FirstOrDefault(c => c.Status == CensusStatus.ToFind)?.Census ?? all[0];
        var place = row?.Check.Place ?? checklist.FirstOrDefault(c => c.Census == initial)?.Place ?? "";
        var form = new CensusEntryViewModel(_tree, p, all, initial, place);
        if (!await _dialogs.EnterCensusAsync(form)) return;
        var lines = form.Lines;
        var touched = new List<Entity> { p };
        touched.AddRange(lines.Select(l => _tree.Person(l.PersonId)).OfType<Person>());
        touched.AddRange(_tree.FamiliesOf(p).Select(x => x.Family));
        var census = form.Census.Census;
        Change($"enter the {census.Name}", touched.Distinct(), () =>
            CensusHousehold.Save(_tree, census, p, form.Place, form.Reference, lines, form.AddOccupations));
        Status = $"Recorded the {census.Name} for {lines.Count(l => l.PersonId != null || l.AddAsNew)} people";
    }

    [RelayCommand]
    private void AddCensusTask(CensusRow row)
    {
        if (_current is not { } p || !row.CanAddTask) return;
        var c = row.Check.Census;
        var item = new ResearchItem
        {
            Description = CensusTask(p, c),
            Category = "Census",
            Note = Display.Join(row.Check.Reason, c.Note).Replace("  ·  ", " "),
        };
        Change("add research task", new Entity[] { p, item }, () =>
        {
            _tree.AddResearch(item);
            p.ResearchIds.Add(item.Id);
        });
    }
}
