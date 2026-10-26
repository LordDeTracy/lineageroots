using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>An alternate name being edited (married name, nickname, other spelling…).</summary>
public sealed partial class NameRow : ObservableObject
{
    public static readonly string[] Kinds = { "Also known as", "Married name", "Birth name", "Nickname", "Religious name", "Immigrant name" };
    private static readonly string[] Codes = { "aka", "married", "birth", "nickname", "religious", "immigrant" };

    public NameRow(Name? n = null)
    {
        _given = n?.Given ?? "";
        _surname = n?.Surname ?? "";
        _suffix = n?.Suffix ?? "";
        _kindIndex = Math.Max(0, Array.IndexOf(Codes, (n?.Type ?? "aka").ToLowerInvariant()));
        Original = n;
    }

    public Name? Original { get; }
    public string[] KindOptions => Kinds;
    [ObservableProperty] private string _given;
    [ObservableProperty] private string _surname;
    [ObservableProperty] private string _suffix;
    [ObservableProperty] private int _kindIndex;

    public bool IsEmpty => Given.Trim().Length == 0 && Surname.Trim().Length == 0;

    public Name ToName()
    {
        var n = Original ?? new Name();
        n.Given = Given.Trim();
        n.Surname = Surname.Trim();
        n.Suffix = Suffix.Trim();
        n.Type = Codes[Math.Clamp(KindIndex, 0, Codes.Length - 1)];
        return n;
    }
}

/// <summary>A DNA test result being edited. Marker values are kept as they were (they come from imports).</summary>
public sealed partial class DnaRow : ObservableObject
{
    public static readonly string[] Kinds = { "Autosomal", "Y-DNA", "mtDNA", "X-DNA" };

    public DnaRow(DnaTest? t = null)
    {
        Original = t;
        _kind = t?.Kind is { Length: > 0 } k ? k : Kinds[0];
        _company = t?.Company ?? "";
        _reference = t?.Reference ?? "";
        _haplogroup = t?.Haplogroup ?? "";
        _date = t?.Date?.ToString("d MMM yyyy") ?? "";
        _note = t?.Note ?? "";
    }

    public DnaTest? Original { get; }
    public string[] KindOptions => Kinds;
    public string MarkerSummary => Original?.Markers.Count is > 0 and var n ? $"{n} markers kept from the original record" : "";
    public bool HasMarkers => MarkerSummary.Length > 0;
    [ObservableProperty] private string _kind;
    [ObservableProperty] private string _company;
    [ObservableProperty] private string _reference;
    [ObservableProperty] private string _haplogroup;
    [ObservableProperty] private string _date;
    [ObservableProperty] private string _note;

    public DnaTest ToTest()
    {
        var t = Original ?? new DnaTest();
        t.Kind = Kind.Trim();
        t.Company = Company.Trim();
        t.Reference = Reference.Trim();
        t.Haplogroup = Haplogroup.Trim();
        t.Note = Note;
        t.Date = DateOnly.TryParse(Date, out var d) ? d : GenDate.Parse(Date) is { Year: > 0 } g
            ? new DateOnly(g.Year, Math.Max(g.Month, 1), Math.Max(g.Day, 1)) : null;
        return t;
    }
}

/// <summary>One LDS ordinance: date, temple and status, stored as an event (or the sealing on the child link).</summary>
public sealed partial class OrdinanceRow : ObservableObject
{
    public static readonly string[] Statuses =
        { "Completed", "Submitted", "Cleared", "Uncleared", "Qualified", "Child", "Infant", "Stillborn", "BIC", "Pre-1970", "DNS", "DNS/CAN" };

    public OrdinanceRow(string tag, string label, Event? e)
    {
        Tag = tag;
        Label = label;
        Original = e;
        _date = e == null ? "" : Display.Date(e.Date);
        _temple = e?.Temple ?? "";
        _status = e?.Status ?? "";
    }

    public string Tag { get; }
    public string Label { get; }
    public Event? Original { get; }
    public IReadOnlyList<string> StatusOptions => Statuses;
    [ObservableProperty] private string _date;
    [ObservableProperty] private string _temple;
    [ObservableProperty] private string _status;

    public bool IsEmpty => Date.Trim().Length == 0 && Temple.Trim().Length == 0 && Status.Trim().Length == 0;

    /// <summary>The event with the edits applied, or null when the row was cleared.</summary>
    public Event? ToEvent()
    {
        if (IsEmpty && (Original == null || (Original.Citations.Count == 0 && Original.Note.Length == 0))) return null;
        var e = Original ?? Event.Of(Tag);
        e.Date = GenDate.Parse(Date);
        e.Temple = Temple.Trim();
        e.Status = Status.Trim();
        return e;
    }
}

/// <summary>
/// Edits a person: name and life dates, alternate names, tags and colour, LDS ordinances, DNA results
/// and contact details. Works on copies; <see cref="Apply"/> writes everything back in one go (so the
/// caller can record it as one undoable edit).
/// </summary>
public sealed partial class PersonEditorViewModel : ObservableObject
{
    private static readonly (string Tag, string Label)[] PersonalOrdinances =
        { ("BAPL", "Baptism"), ("CONL", "Confirmation"), ("WAC", "Initiatory"), ("ENDL", "Endowment") };

    private readonly Person _person;
    private readonly ChildLink? _parentLink;

    public PersonEditorViewModel(Person person, string title, FamilyTree? tree = null)
    {
        _person = person;
        Title = title;
        var n = person.PrimaryName;
        _given = n.Given;
        _surname = n.Surname;
        _suffix = n.Suffix;
        _sexIndex = (int)person.Sex;
        _birthDate = Display.Date(person.Find("BIRT")?.Date ?? GenDate.Empty);
        _birthPlace = person.Find("BIRT")?.Place ?? "";
        _deathDate = Display.Date(person.Find("DEAT")?.Date ?? GenDate.Empty);
        _deathPlace = person.Find("DEAT")?.Place ?? "";
        _note = person.Note;
        _personTitle = person.Title;
        _description = person.Description;
        _email = person.Email;
        _phone = person.Phone;
        _address = person.Address;
        _url = person.Url;
        _userId = person.UserId;
        _familySearchId = person.FamilySearchId;
        _colourIndex = Array.IndexOf(Display.Colours, person.Colour) + 1;   // 0 = no colour

        foreach (var alt in person.Names.Skip(1)) AltNames.Add(new NameRow(alt));
        foreach (var t in person.Tags) Tags.Add(t);
        foreach (var d in person.DnaTests) Dna.Add(new DnaRow(d));
        foreach (var (tag, label) in PersonalOrdinances) Ordinances.Add(new OrdinanceRow(tag, label, person.Find(tag)));

        // Relationship to the primary parents, and the LDS sealing to them, live on the child link.
        var family = tree?.Family(person.ParentFamilyIds.FirstOrDefault());
        _parentLink = family?.Children.FirstOrDefault(c => c.ChildId == person.Id);
        if (_parentLink != null && tree != null)
        {
            ParentsText = string.Join(" and ", new[] { tree.Person(family!.Partner1Id)?.DisplayName, tree.Person(family.Partner2Id)?.DisplayName }.OfType<string>());
            _fatherRelation = (int)_parentLink.ToFather;
            _motherRelation = (int)_parentLink.ToMother;
            Sealing = new OrdinanceRow("SLGC", "Sealed to parents", _parentLink.Sealing);
        }
        else Sealing = new OrdinanceRow("SLGC", "Sealed to parents", null);

        TagSuggestions = tree == null ? Array.Empty<string>()
            : tree.People.Values.SelectMany(p => p.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }

    public string Title { get; }
    public string[] SexOptions { get; } = { "Unknown", "Male", "Female" };
    public string[] Tabs { get; } = { "Basics", "Names", "Tags", "LDS", "DNA", "More" };
    public string[] RelationOptions { get; } = { "Birth", "Adopted", "Step", "Foster", "Guardian", "Sealing" };
    public IReadOnlyList<string> ColourOptions { get; } = new[] { "None" }.Concat(Display.Colours).ToList();
    public IReadOnlyList<string> TagSuggestions { get; }
    public string ParentsText { get; } = "";
    public bool HasParents => _parentLink != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBasicsTab), nameof(IsNamesTab), nameof(IsTagsTab), nameof(IsLdsTab), nameof(IsDnaTab), nameof(IsMoreTab))]
    private int _tabIndex;
    public bool IsBasicsTab => TabIndex == 0;
    public bool IsNamesTab => TabIndex == 1;
    public bool IsTagsTab => TabIndex == 2;
    public bool IsLdsTab => TabIndex == 3;
    public bool IsDnaTab => TabIndex == 4;
    public bool IsMoreTab => TabIndex == 5;

    [ObservableProperty] private string _given;
    [ObservableProperty] private string _surname;
    [ObservableProperty] private string _suffix;
    [ObservableProperty] private int _sexIndex;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BirthDateHint), nameof(HasBirthDateHint))] private string _birthDate;
    [ObservableProperty] private string _birthPlace;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DeathDateHint), nameof(HasDeathDateHint))] private string _deathDate;
    [ObservableProperty] private string _deathPlace;
    [ObservableProperty] private string _note;
    [ObservableProperty] private string _personTitle;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _email;
    [ObservableProperty] private string _phone;
    [ObservableProperty] private string _address;
    [ObservableProperty] private string _url;
    [ObservableProperty] private string _userId;
    [ObservableProperty] private string _familySearchId;
    [ObservableProperty] private int _colourIndex;
    [ObservableProperty] private int _fatherRelation;
    [ObservableProperty] private int _motherRelation;
    [ObservableProperty] private string _newTag = "";

    public ObservableCollection<NameRow> AltNames { get; } = new();
    public ObservableCollection<string> Tags { get; } = new();
    public ObservableCollection<DnaRow> Dna { get; } = new();
    public ObservableCollection<OrdinanceRow> Ordinances { get; } = new();
    public OrdinanceRow Sealing { get; }

    public string BirthDateHint => Hint(BirthDate);
    public string DeathDateHint => Hint(DeathDate);
    public bool HasBirthDateHint => BirthDateHint.Length > 0;
    public bool HasDeathDateHint => DeathDateHint.Length > 0;

    private static string Hint(string text) => Display.DateHint(text);

    [RelayCommand] private void AddName() => AltNames.Add(new NameRow { Surname = Surname });
    [RelayCommand] private void RemoveName(NameRow row) => AltNames.Remove(row);
    [RelayCommand] private void AddDna() => Dna.Add(new DnaRow());
    [RelayCommand] private void RemoveDna(DnaRow row) => Dna.Remove(row);

    [RelayCommand]
    private void AddTag()
    {
        var t = NewTag.Trim();
        if (t.Length > 0 && !Tags.Contains(t, StringComparer.OrdinalIgnoreCase)) Tags.Add(t);
        NewTag = "";
    }

    [RelayCommand] private void RemoveTag(string tag) => Tags.Remove(tag);

    public void Apply()
    {
        var n = _person.PrimaryName;
        n.Given = Given.Trim();
        n.Surname = Surname.Trim();
        n.Suffix = Suffix.Trim();
        _person.Sex = (Sex)SexIndex;
        SetEvent("BIRT", BirthDate, BirthPlace);
        SetEvent("DEAT", DeathDate, DeathPlace);
        _person.Note = Note;
        _person.Title = PersonTitle.Trim();
        _person.Description = Description;
        _person.Email = Email.Trim();
        _person.Phone = Phone.Trim();
        _person.Address = Address;
        _person.Url = Url.Trim();
        _person.UserId = UserId.Trim();
        _person.FamilySearchId = FamilySearchId.Trim().ToUpperInvariant();
        _person.Colour = ColourIndex > 0 ? Display.Colours[ColourIndex - 1] : "";

        var primary = _person.PrimaryName;
        _person.Names.Clear();
        _person.Names.Add(primary);
        _person.Names.AddRange(AltNames.Where(r => !r.IsEmpty).Select(r => r.ToName()));

        _person.Tags.Clear();
        _person.Tags.AddRange(Tags);

        _person.DnaTests.Clear();
        _person.DnaTests.AddRange(Dna.Select(d => d.ToTest()));

        foreach (var row in Ordinances)
        {
            var e = row.ToEvent();
            if (row.Original != null && e == null) _person.Events.Remove(row.Original);
            else if (row.Original == null && e != null) _person.Events.Add(e);
        }

        if (_parentLink != null)
        {
            _parentLink.ToFather = (ParentRelation)FatherRelation;
            _parentLink.ToMother = (ParentRelation)MotherRelation;
            _parentLink.Sealing = Sealing.ToEvent();
        }
    }

    private void SetEvent(string tag, string date, string place)
    {
        var e = _person.Find(tag);
        var empty = string.IsNullOrWhiteSpace(date) && string.IsNullOrWhiteSpace(place);
        if (e == null && empty) return;
        if (e == null)
        {
            e = Event.Of(tag);
            var insertAt = tag == "BIRT" ? 0 : _person.Events.Count;
            _person.Events.Insert(insertAt, e);
        }
        e.Date = GenDate.Parse(date);
        e.Place = place.Trim();
        if (empty && e.Citations.Count == 0 && e.Note.Length == 0) _person.Events.Remove(e);
    }
}
