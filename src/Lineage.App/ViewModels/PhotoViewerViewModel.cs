using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.App.Controls;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>Someone who can be tagged, as the name picker shows them.</summary>
public sealed record PersonOption(long Id, string Name, string Lifespan)
{
    public bool HasLifespan => Lifespan.Length > 0;
    /// <summary>What the picker filters on and shows once chosen.</summary>
    public override string ToString() => Lifespan.Length > 0 ? $"{Name} ({Lifespan})" : Name;
}

/// <summary>One photo the user changed: write <see cref="Edited"/> into <see cref="Original"/> inside an undoable edit.</summary>
public sealed record PhotoEdit(MediaLink Original, MediaLink Edited)
{
    /// <summary>Copies the edits into the original link, which stays in its list.</summary>
    public void Apply() => Original.CopyFrom(Edited);
}

/// <summary>A tagged person in the current photo: a row in the list and a rectangle on the photo.</summary>
public sealed partial class TagRow : ObservableObject, ITaggedRegion
{
    public TagRow(PhotoRegion region, Person? person)
    {
        Region = region;
        Exists = person != null;
        Name = person?.DisplayName ?? "Someone no longer in the tree";
        Lifespan = person?.Lifespan ?? "";
        Initials = person != null ? Display.Initials(person) : "?";
        IsMale = person?.Sex == Sex.Male;
        IsFemale = person?.Sex == Sex.Female;
    }

    public PhotoRegion Region { get; }
    public long PersonId => Region.PersonId;
    public bool Exists { get; }
    public string Name { get; }
    public string Lifespan { get; }
    public bool HasLifespan => Lifespan.Length > 0;
    public string Initials { get; }
    public bool IsMale { get; }
    public bool IsFemale { get; }
    public Rect Bounds => new(Region.X, Region.Y, Region.Width, Region.Height);
    public string Label => Name;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// A large view of a set of photos, one at a time, where the user edits the caption details and
/// tags people by drawing a box around them. Works on deep copies: nothing in the tree changes
/// until the caller applies <see cref="Results"/> (as one undoable edit) after the user clicks Done.
/// </summary>
public sealed partial class PhotoViewerViewModel : ObservableObject, IDisposable
{
    /// <summary>Longest side photos are decoded at; enough for a large screen without holding huge scans in memory.</summary>
    public const int MaxSide = 2400;

    private readonly FamilyTree _tree;
    private readonly IReadOnlyList<MediaLink> _originals;
    private readonly List<MediaLink> _photos;
    private readonly Func<string, string> _resolvePath;
    private bool _showing;
    private int _loadVersion;

    /// <param name="photos">The links to show, as they are in the tree; they are not changed.</param>
    /// <param name="resolvePath">Turns a link's File into a full path (e.g. MediaLibrary.Resolve with the tree file).</param>
    public PhotoViewerViewModel(FamilyTree tree, IReadOnlyList<MediaLink> photos, int startIndex, Func<string, string> resolvePath)
    {
        if (photos.Count == 0) throw new ArgumentException("There must be at least one photo to show.", nameof(photos));
        _tree = tree;
        _originals = photos;
        _photos = photos.Select(p => p.Clone()).ToList();
        _resolvePath = resolvePath;
        People = tree.People.Values.OrderBy(p => p.PrimaryName.SortKey).ThenBy(p => p.Id)
                     .Select(p => new PersonOption(p.Id, p.DisplayName, p.Lifespan)).ToList();
        _index = Math.Clamp(startIndex, 0, photos.Count - 1);
        Show();
    }

    /// <summary>Raised when the user clicks a tagged person's name; the window closes as if Done was clicked.</summary>
    public event Action<long>? OpenPersonRequested;
    /// <summary>The person whose name was clicked, if any — navigate to them after applying the results.</summary>
    public long? OpenPersonId { get; private set; }

    /// <summary>Everyone in the tree, for the name picker.</summary>
    public IReadOnlyList<PersonOption> People { get; }
    public ObservableCollection<TagRow> Tags { get; } = new();

    /// <summary>Photos the user changed, original with edited copy, in the order shown.</summary>
    public IReadOnlyList<PhotoEdit> Results =>
        _photos.Select((edited, i) => new PhotoEdit(_originals[i], edited)).Where(e => !Same(e.Original, e.Edited)).ToList();

    private static bool Same(MediaLink a, MediaLink b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    // ---- Which photo ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Position), nameof(HasPrevious), nameof(HasNext), nameof(Current))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    private int _index;

    public int Count => _photos.Count;
    public bool HasMany => Count > 1;
    public string Position => $"Photo {Index + 1} of {Count}";
    public bool HasPrevious => Index > 0;
    public bool HasNext => Index < Count - 1;
    /// <summary>The edited copy of the photo on screen.</summary>
    public MediaLink Current => _photos[Index];
    [ObservableProperty] private string _fullPath = "";
    [ObservableProperty] private string _fileName = "";

    partial void OnIndexChanged(int value) => Show();

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private void Previous() => Index--;

    [RelayCommand(CanExecute = nameof(HasNext))]
    private void Next() => Index++;

    // ---- The picture ----

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasImage))] private Bitmap? _image;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasMessage))] private string _messageTitle = "";
    [ObservableProperty] private string _messageDetail = "";

    public bool HasImage => Image != null;
    public bool HasMessage => MessageTitle.Length > 0;
    /// <summary>The image load for the current photo; tests await it.</summary>
    internal Task ImageTask { get; private set; } = Task.CompletedTask;

    // ---- Caption details (written into the copy as they are typed) ----

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Heading))] private string _title = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TakenHint), nameof(HasTakenHint))] private string _taken = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LocationError), nameof(HasLocationError))] private string _location = "";

    public string Heading => Title.Trim().Length > 0 ? Title.Trim() : Path.GetFileNameWithoutExtension(FileName);
    public string TakenHint => Display.DateHint(Taken);
    public bool HasTakenHint => TakenHint.Length > 0;
    public string LocationError => Location.Trim().Length > 0 && GeoPoint.Parse(Location) == null
        ? "Couldn't read these coordinates — try 42.2626, -71.8023" : "";
    public bool HasLocationError => LocationError.Length > 0;

    partial void OnTitleChanged(string value) { if (!_showing) Current.Title = value.Trim(); }
    partial void OnNoteChanged(string value) { if (!_showing) Current.Note = value; }

    partial void OnTakenChanged(string value)
    {
        if (_showing) return;
        var text = value.Trim();
        var date = GenDate.Parse(text);
        Current.Taken = text.Length == 0 ? "" : date.Kind == DateKind.Text ? text : date.ToGedcom();
    }

    partial void OnLocationChanged(string value)
    {
        if (_showing) return;
        if (value.Trim().Length == 0) Current.Location = null;
        else if (GeoPoint.Parse(value) is { } at) Current.Location = at;
    }

    // ---- Tagging ----

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowEmpty))] private bool _isTagging;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingRegion), nameof(IsDrawing))]
    private Rect? _pendingRegion;
    [ObservableProperty] private PersonOption? _pendingPerson;
    [ObservableProperty] private string _pendingText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTagError))] private string _tagError = "";
    [ObservableProperty] private TagRow? _highlightedTag;

    public bool HasPendingRegion => PendingRegion != null;
    /// <summary>Tagging, but no box drawn yet.</summary>
    public bool IsDrawing => IsTagging && PendingRegion == null;
    public bool HasTagError => TagError.Length > 0;

    /// <summary>Faces found in the photo that aren't tagged yet, normalised; click one to tag it.</summary>
    [ObservableProperty] private IReadOnlyList<Rect> _faceBoxes = Array.Empty<Rect>();
    private List<Lineage.Core.Media.FaceBox> _faces = new();
    public bool HasFaceBoxes => FaceBoxes.Count > 0;
    public string FaceHint => FaceBoxes.Count switch
    {
        0 => "",
        1 => "1 face found that isn't tagged yet: click it on the photo to say who it is.",
        var n => $"{n} faces found that aren't tagged yet: click one on the photo to say who it is.",
    };
    /// <summary>The face search for the current photo; tests await it.</summary>
    internal Task FaceTask { get; private set; } = Task.CompletedTask;

    partial void OnFaceBoxesChanged(IReadOnlyList<Rect> value)
    {
        OnPropertyChanged(nameof(HasFaceBoxes));
        OnPropertyChanged(nameof(FaceHint));
    }

    /// <summary>Starts tagging the face clicked on the photo, with a box a little larger than the face.</summary>
    [RelayCommand]
    private void TagFace(Rect face)
    {
        ClearPending();
        IsTagging = true;
        var (dx, dy) = (face.Width * 0.15, face.Height * 0.12);
        var x = Math.Max(0, face.X - dx);
        var y = Math.Max(0, face.Y - dy);
        PendingRegion = new Rect(x, y, Math.Min(1 - x, face.Width + 2 * dx), Math.Min(1 - y, face.Height + 2 * dy));
    }

    /// <summary>The faces not already covered by a tag.</summary>
    private void RefreshFaceBoxes() =>
        FaceBoxes = _faces.Where(f => !Current.Regions.Any(r =>
                f.Overlap(r.X, r.Y, r.Width, r.Height) > 0.15
                || (f.X + f.Width / 2 >= r.X && f.X + f.Width / 2 <= r.X + r.Width && f.Y + f.Height / 2 >= r.Y && f.Y + f.Height / 2 <= r.Y + r.Height)))
            .Select(f => new Rect(f.X, f.Y, f.Width, f.Height)).ToList();
    public bool HasTags => Tags.Count > 0;
    public bool ShowEmpty => !HasTags && !IsTagging;
    public string TaggingHint => PendingRegion == null
        ? "Drag a box around the person’s face  ·  Esc to stop"
        : "Now choose who this is";

    partial void OnIsTaggingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDrawing));
        if (!value) ClearPending();
    }

    partial void OnPendingRegionChanged(Rect? value)
    {
        TagError = "";
        OnPropertyChanged(nameof(TaggingHint));
    }

    partial void OnHighlightedTagChanged(TagRow? value)
    {
        foreach (var row in Tags) row.IsHighlighted = ReferenceEquals(row, value);
    }

    [RelayCommand]
    private void StartTagging()
    {
        ClearPending();
        IsTagging = true;
    }

    [RelayCommand]
    private void CancelTagging() => IsTagging = false;

    /// <summary>Tags the chosen person in the box just drawn, then waits for the next box.</summary>
    [RelayCommand]
    private void ConfirmTag()
    {
        if (PendingRegion is not { } box) { TagError = "Drag a box around the person on the photo first."; return; }
        var person = PendingPerson ?? Match(PendingText);
        if (person == null)
        {
            TagError = PendingText.Trim().Length == 0
                ? "Type a name, then pick the person from the list."
                : "No one in the tree matches that — pick a name from the list.";
            return;
        }
        AddTag(person.Id, box);
        ClearPending();
    }

    /// <summary>Tags a person in the current photo; tagging someone already tagged moves their box.</summary>
    public void AddTag(long personId, Rect box)
    {
        static double R(double v) => Math.Round(Math.Clamp(v, 0, 1), 4);   // GEDCOM keeps 4 decimals
        var x = R(box.X);
        var y = R(box.Y);
        var region = new PhotoRegion
        {
            PersonId = personId, X = x, Y = y,
            Width = Math.Min(R(box.Width), Math.Round(1 - x, 4)), Height = Math.Min(R(box.Height), Math.Round(1 - y, 4)),
        };
        var at = Current.Regions.FindIndex(r => r.PersonId == personId);
        if (at >= 0) Current.Regions[at] = region; else Current.Regions.Add(region);
        RefreshTags();
    }

    [RelayCommand]
    private void RemoveTag(TagRow row)
    {
        Current.Regions.Remove(row.Region);
        RefreshTags();
    }

    [RelayCommand]
    private void OpenPerson(TagRow row)
    {
        if (!row.Exists) return;
        OpenPersonId = row.PersonId;
        OpenPersonRequested?.Invoke(row.PersonId);
    }

    /// <summary>The one person the typed text names, if it names exactly one.</summary>
    private PersonOption? Match(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return null;
        var exact = People.Where(p => string.Equals(p.ToString(), t, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(p.Name, t, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (exact.Count > 0) return exact.Count == 1 ? exact[0] : null;
        var partial = People.Where(p => p.ToString().Contains(t, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private void ClearPending()
    {
        PendingRegion = null;
        PendingPerson = null;
        PendingText = "";
        TagError = "";
    }

    private void RefreshTags()
    {
        Tags.Clear();
        foreach (var r in Current.Regions.OrderBy(r => r.X)) Tags.Add(new TagRow(r, _tree.Person(r.PersonId)));   // left to right
        HighlightedTag = null;
        RefreshFaceBoxes();
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    // ---- Showing a photo ----

    private void Show()
    {
        IsTagging = false;
        ClearPending();
        _showing = true;
        FullPath = _resolvePath(Current.File);
        FileName = Path.GetFileName(FullPath);
        Title = Current.Title;
        Taken = Current.Taken.Length > 0 ? Display.Date(GenDate.Parse(Current.Taken)) : "";
        Note = Current.Note;
        Location = Current.Location?.ToString() ?? "";
        _showing = false;
        OnPropertyChanged(nameof(Heading));
        RefreshTags();
        ImageTask = LoadImageAsync();
    }

    private async Task LoadImageAsync()
    {
        var version = ++_loadVersion;
        var path = FullPath;
        var old = Image;
        Image = null;
        old?.Dispose();
        _faces = new();
        RefreshFaceBoxes();
        FaceTask = Task.CompletedTask;
        MessageTitle = MessageDetail = "";
        IsLoading = false;   // an earlier load still running no longer counts
        if (!File.Exists(path))
        {
            MessageTitle = "Photo not found";
            MessageDetail = $"The file isn’t at\n{path}\nIt may have been moved, renamed, or be on a drive that isn’t connected.";
            return;
        }
        IsLoading = true;
        var bitmap = await Task.Run(() => Decode(path));
        if (version != _loadVersion) { bitmap?.Dispose(); return; }   // the user moved on meanwhile
        IsLoading = false;
        if (bitmap == null)
        {
            MessageTitle = "This file can’t be shown";
            MessageDetail = $"{Path.GetFileName(path)} isn’t a picture this app can open.";
            return;
        }
        Image = bitmap;
        FaceTask = FindFacesAsync(path, version);
    }

    private async Task FindFacesAsync(string path, int version)
    {
        var faces = await Task.Run(() => FaceFinder.Find(path));
        if (version != _loadVersion) return;
        ShowFaces(faces);
    }

    /// <summary>Shows faces found in the current photo (those not tagged yet).</summary>
    internal void ShowFaces(List<Lineage.Core.Media.FaceBox> faces)
    {
        _faces = faces;
        RefreshFaceBoxes();
    }

    /// <summary>Decodes a photo, scaling very large ones down to <see cref="MaxSide"/> as they are read.</summary>
    private static Bitmap? Decode(string path)
    {
        try
        {
            var (w, h) = (0, 0);
            try
            {
                using var codec = SkiaSharp.SKCodec.Create(path);
                if (codec != null) (w, h) = (codec.Info.Width, codec.Info.Height);
            }
            catch { /* unknown size: decode it as it is */ }
            using var stream = File.OpenRead(path);
            if (w <= MaxSide && h <= MaxSide) return new Bitmap(stream);
            return w >= h
                ? Bitmap.DecodeToWidth(stream, MaxSide, BitmapInterpolationMode.HighQuality)
                : Bitmap.DecodeToHeight(stream, MaxSide, BitmapInterpolationMode.HighQuality);
        }
        catch { return null; }   // not an image, or damaged
    }

    /// <summary>Frees the decoded photo; the window calls this when it closes.</summary>
    public void Dispose()
    {
        _loadVersion++;
        var old = Image;
        Image = null;
        old?.Dispose();
    }
}
