using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed record EventTypeOption(string Tag, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A citation being edited: which source, and where in it.</summary>
public sealed partial class CitationRow : ObservableObject
{
    public CitationRow(string sourceTitle, Citation citation)
    {
        SourceTitle = sourceTitle;
        Citation = citation;
        _page = citation.Page;
        _qualityIndex = (citation.Quality ?? -1) + 1;
    }

    public string SourceTitle { get; }
    public Citation Citation { get; }
    public static string[] Qualities { get; } = { "Not rated", "Unreliable", "Questionable", "Secondary evidence", "Direct evidence" };
    [ObservableProperty] private string _page;
    [ObservableProperty] private int _qualityIndex;

    public Citation ToCitation() => new()
    {
        SourceId = Citation.SourceId, Page = Page.Trim(), Text = Citation.Text, Note = Citation.Note,
        Quality = QualityIndex > 0 ? QualityIndex - 1 : null,
    };
}

public enum EditorResult { Cancelled, Saved, Deleted }

/// <summary>Edits one event of a person or a family. Works on copies; <see cref="Apply"/> writes back.</summary>
public sealed partial class EventEditorViewModel : ObservableObject
{
    public static readonly EventTypeOption[] PersonTypes =
    {
        new("BIRT", "Birth"), new("CHR", "Christening"), new("BAPM", "Baptism"), new("DEAT", "Death"),
        new("BURI", "Burial"), new("CREM", "Cremation"), new("OCCU", "Occupation"), new("RESI", "Residence"),
        new("CENS", "Census"), new("EDUC", "Education"), new("GRAD", "Graduation"), new("MILI", "Military service"),
        new("EMIG", "Emigration"), new("IMMI", "Immigration"), new("NATU", "Naturalization"), new("RELI", "Religion"),
        new("RETI", "Retirement"), new("WILL", "Will"), new("PROB", "Probate"), new("ADOP", "Adoption"),
        new("CONF", "Confirmation"), new("FCOM", "First communion"), new("ORDN", "Ordination"),
        new("BARM", "Bar Mitzvah"), new("BASM", "Bas Mitzvah"), new("BLES", "Blessing"), new("NATI", "Nationality"),
        new("PROP", "Property"), new("BAPL", "Baptism (LDS)"), new("CONL", "Confirmation (LDS)"),
        new("WAC", "Initiatory (LDS)"), new("ENDL", "Endowment (LDS)"), new("SLGC", "Sealing to parents (LDS)"),
        new("EVEN", "Other (type your own)…"),
    };

    public static readonly EventTypeOption[] FamilyTypes =
    {
        new("MARR", "Marriage"), new("ENGA", "Engagement"), new("MARB", "Marriage banns"),
        new("MARL", "Marriage license"), new("MARC", "Marriage contract"), new("MARS", "Marriage settlement"),
        new("RESI", "Residence"), new("CENS", "Census"), new("DIV", "Divorce"), new("DIVF", "Divorce filed"),
        new("ANUL", "Annulment"), new("SLGS", "Sealing to spouse (LDS)"), new("EVEN", "Other (type your own)…"),
    };

    private readonly FamilyTree _tree;

    public EventEditorViewModel(FamilyTree tree, Event e, bool isNew, bool forFamily, string title)
    {
        _tree = tree;
        Title = title;
        IsNew = isNew;
        Types = forFamily ? FamilyTypes : PersonTypes;
        _type = Types.FirstOrDefault(t => t.Tag == e.Tag) ?? Types[^1];
        _customName = e.Tag == "EVEN" ? e.TypeName : Types.Contains(_type) && _type.Tag != "EVEN" ? "" : EventTypes.Label(e);
        _date = Display.Date(e.Date);
        _place = e.Place;
        _coordinates = e.Location?.ToString() ?? "";
        _value = e.Value == "Y" ? "" : e.Value;
        _cause = e.Cause;
        _temple = e.Temple;
        _status = e.Status;
        _note = e.Note;
        _private = e.Private;
        foreach (var c in e.Citations)
            Citations.Add(new CitationRow(_tree.Sources.TryGetValue(c.SourceId, out var s) ? s.Title : "(missing source)", c));
        SourceTitles = _tree.Sources.Values.Select(s => s.Title).Where(t => t.Length > 0).Distinct().Order().ToList();
    }

    public string Title { get; }
    public bool IsNew { get; }
    public bool CanDelete => !IsNew;
    public IReadOnlyList<EventTypeOption> Types { get; }
    public ObservableCollection<CitationRow> Citations { get; } = new();
    public IReadOnlyList<string> SourceTitles { get; }
    public IReadOnlyList<string> Qualities => CitationRow.Qualities;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom), nameof(ValueLabel), nameof(ShowCause), nameof(ShowLds), nameof(ShowValue))]
    private EventTypeOption _type;
    [ObservableProperty] private string _customName;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DateHint), nameof(HasDateHint))] private string _date;
    [ObservableProperty] private string _place;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CoordinatesError), nameof(HasCoordinatesError))] private string _coordinates;
    [ObservableProperty] private string _value;
    [ObservableProperty] private string _cause;
    [ObservableProperty] private string _temple;
    [ObservableProperty] private string _status;
    [ObservableProperty] private string _note;
    [ObservableProperty] private bool _private;
    [ObservableProperty] private string _newSourceTitle = "";
    [ObservableProperty] private string _newPage = "";

    public bool IsCustom => Type.Tag == "EVEN";
    /// <summary>Births, deaths and the like have no free-text value; occupations, residences etc. do.</summary>
    public bool ShowValue => IsCustom || Value.Length > 0 || Type.Tag is "OCCU" or "EDUC" or "RELI" or "RESI" or "MILI" or "PROP"
                             or "NATI" or "GRAD" or "CENS" or "ORDN" or "CAST" or "NCHI";
    public bool ShowCause => Type.Tag is "DEAT" or "BURI" or "CREM";
    public bool ShowLds => EventTypes.IsLds(Type.Tag);
    public string DateHint => Display.DateHint(Date);
    public bool HasDateHint => DateHint.Length > 0;
    public string CoordinatesError => Coordinates.Trim().Length > 0 && GeoPoint.Parse(Coordinates) == null
        ? "Couldn't read these coordinates — try 42.2626, -71.8023" : "";
    public bool HasCoordinatesError => CoordinatesError.Length > 0;

    /// <summary>What the free-text value means for this kind of event.</summary>
    public string ValueLabel => Type.Tag switch
    {
        "OCCU" => "Occupation", "EDUC" => "School or qualification", "RELI" => "Religion", "RESI" => "Address",
        "MILI" => "Unit, rank or war", "PROP" => "Property", "NATI" => "Nationality", "GRAD" => "School or degree",
        "CENS" => "Household or reference", "ORDN" => "Office", _ => "Description",
    };

    // ---- photos of the event (e.g. a wedding photo, a gravestone) ----

    private readonly List<MediaLink> _media = new();
    private Func<string, string> _resolve = f => f;
    private Func<Task<List<MediaLink>>>? _pick;
    public ObservableCollection<PhotoTile> Photos { get; } = new();
    public bool CanAddPhotos => _pick != null;

    /// <summary>Called by the main window: how to find photo files and how to pick new ones.</summary>
    public void AttachPhotos(Event e, Func<string, string> resolve, Func<Task<List<MediaLink>>> pick)
    {
        _resolve = resolve;
        _pick = pick;
        _media.Clear();
        _media.AddRange(e.Media);
        RefreshPhotos();
        OnPropertyChanged(nameof(CanAddPhotos));
    }

    private void RefreshPhotos()
    {
        Photos.Clear();
        foreach (var m in _media) Photos.Add(new PhotoTile(m, _resolve(m.File)));
        foreach (var t in Photos) _ = t.LoadAsync(200);
    }

    [RelayCommand]
    private async Task AddPhotosAsync()
    {
        if (_pick == null) return;
        _media.AddRange(await _pick());
        RefreshPhotos();
    }

    [RelayCommand]
    private void RemovePhoto(PhotoTile tile)
    {
        _media.Remove(tile.Media);
        RefreshPhotos();
    }

    /// <summary>Sources typed here that don't exist yet are created on save.</summary>
    public List<string> NewSources { get; } = new();

    [RelayCommand]
    private void AddCitation()
    {
        var title = NewSourceTitle.Trim();
        if (title.Length == 0) return;
        var existing = _tree.Sources.Values.FirstOrDefault(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));
        if (existing == null && !NewSources.Contains(title, StringComparer.OrdinalIgnoreCase)) NewSources.Add(title);
        Citations.Add(new CitationRow(existing?.Title ?? title, new Citation { SourceId = existing?.Id ?? 0, Page = NewPage.Trim() }));
        NewSourceTitle = "";
        NewPage = "";
    }

    [RelayCommand]
    private void RemoveCitation(CitationRow row) => Citations.Remove(row);

    public bool IsValid => !HasCoordinatesError && (!IsCustom || CustomName.Trim().Length > 0);

    /// <summary>Writes the edits into <paramref name="e"/>. New sources named in citations are created in the tree.</summary>
    public void Apply(Event e)
    {
        e.Tag = Type.Tag;
        e.TypeName = IsCustom ? CustomName.Trim() : "";
        e.Date = GenDate.Parse(Date);
        e.Place = Place.Trim();
        e.Location = GeoPoint.Parse(Coordinates);
        if (e.Tag != "DIV" || Value.Trim().Length > 0) e.Value = Value.Trim();
        e.Cause = ShowCause ? Cause.Trim() : e.Cause;
        e.Temple = ShowLds ? Temple.Trim() : e.Temple;
        e.Status = ShowLds ? Status.Trim() : e.Status;
        e.Note = Note;
        e.Private = Private;
        if (_pick != null)
        {
            e.Media.Clear();
            e.Media.AddRange(_media);
        }
        e.Citations.Clear();
        foreach (var row in Citations)
        {
            var c = row.ToCitation();
            if (c.SourceId == 0)
            {
                var source = _tree.Sources.Values.FirstOrDefault(s => string.Equals(s.Title, row.SourceTitle, StringComparison.OrdinalIgnoreCase))
                             ?? _tree.AddSource(new Source { Title = row.SourceTitle });
                c.SourceId = source.Id;
            }
            e.Citations.Add(c);
        }
    }
}
