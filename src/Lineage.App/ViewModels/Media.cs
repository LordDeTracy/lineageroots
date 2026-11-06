using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Media;
using Lineage.Core.Model;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Lineage.App.ViewModels;

/// <summary>
/// A photo (or sound recording, video or document) attached to a person. Photos get a thumbnail that loads in the
/// background; the others show what they are and open in the computer's own player or viewer.
/// </summary>
public sealed partial class PhotoTile : ObservableObject
{
    public const string FileTypes = "*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp";
    /// <summary>Everything that can be added: photos, sound, video and documents.</summary>
    public const string AllTypes = FileTypes + ";" + MediaKinds.SoundTypes + ";" + MediaKinds.VideoTypes + ";" + MediaKinds.DocumentTypes;
    private static readonly ConcurrentDictionary<(string, int), Bitmap?> Cache = new();

    public PhotoTile(MediaLink media) : this(media, media.File) { }

    /// <param name="fullPath">Where the file is: links can be relative to the tree file (see MediaLibrary.Resolve).</param>
    public PhotoTile(MediaLink media, string fullPath)
    {
        Media = media;
        FullPath = fullPath;
        Title = media.Title.Length > 0 ? media.Title : Path.GetFileNameWithoutExtension(media.File);
        var taken = media.Taken.Length > 0 ? Display.Date(GenDate.Parse(media.Taken)) : "";
        Kind = MediaKinds.Of(media);
        Caption = taken.Length > 0 ? taken : media.Location != null ? "Location saved" : Kind switch
        {
            MediaKind.Sound => "Sound recording",
            MediaKind.Video => "Video",
            MediaKind.Document => "Document",
            MediaKind.Other => Path.GetExtension(media.File).TrimStart('.').ToUpperInvariant(),
            _ => "",
        };
        IsMissing = !File.Exists(fullPath);
    }

    public MediaKind Kind { get; }
    public bool IsPicture => Kind == MediaKind.Photo;
    public bool IsSound => Kind == MediaKind.Sound;
    public bool IsVideo => Kind == MediaKind.Video;
    /// <summary>Sound or video: played in the computer's own player.</summary>
    public bool IsPlayable => Kind is MediaKind.Sound or MediaKind.Video;
    /// <summary>Anything but a photo: opened in another app.</summary>
    public bool IsFile => Kind != MediaKind.Photo;
    public bool IsDocument => Kind is MediaKind.Document or MediaKind.Other;
    public bool CanBeProfile => IsPicture && !IsProfile;
    public bool IsPrivate => Media.Private;

    public MediaLink Media { get; }
    /// <summary>The file on disk; open this rather than <see cref="MediaLink.File"/>, which may be relative.</summary>
    public string FullPath { get; }
    /// <summary>Someone is tagged in the photo.</summary>
    public bool HasTags => Media.Regions.Count > 0;
    public string Title { get; }
    public string Caption { get; }
    public bool HasCaption => Caption.Length > 0;
    public bool IsProfile => Media.Primary;
    public bool HasLocation => Media.Location != null;
    public bool IsMissing { get; }
    public string MapUrl => Media.Location?.OpenStreetMapUrl ?? "";
    public string Spoken => Display.Join(Title, Caption, IsProfile ? "profile photo" : "").Replace("  ·  ", ", ");

    [ObservableProperty] private Bitmap? _thumbnail;

    public async Task LoadAsync(int width)
    {
        if (IsMissing || !IsPicture) return;
        var path = FullPath;
        Thumbnail = await Task.Run(() => Cache.GetOrAdd((path, width), key =>
        {
            try
            {
                using var stream = File.OpenRead(key.Item1);
                return Bitmap.DecodeToWidth(stream, key.Item2, BitmapInterpolationMode.MediumQuality);
            }
            catch { return null; } // unreadable or unsupported image: the tile shows a placeholder
        }));
    }

    /// <summary>Builds the link for a newly added photo, reading where and when it was taken from its EXIF data.</summary>
    public static MediaLink FromFile(string path)
    {
        var kind = MediaKinds.Of(path);
        var link = new MediaLink
        {
            File = path,
            Title = Path.GetFileNameWithoutExtension(path),
            Format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            MediaType = MediaKinds.GedcomType(kind == MediaKind.Other ? MediaKind.Photo : kind),
        };
        try { link.Size = new FileInfo(path).Length; }
        catch (IOException) { /* size only helps relinking later */ }
        catch (UnauthorizedAccessException) { }
        if (kind is not (MediaKind.Photo or MediaKind.Other)) return link;   // camera details are only read from pictures
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);
            if (dirs.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation() is { IsZero: false } at)
                link.Location = new GeoPoint(at.Latitude, at.Longitude);
            if (dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault() is { } exif &&
                exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var when))
                link.Taken = when.ToString("d MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        }
        catch { /* no readable metadata — the photo is still added */ }
        return link;
    }

    public static bool IsPhoto(MediaLink m) => MediaKinds.Of(m) == MediaKind.Photo;
}

/// <summary>
/// One place the person is connected to, with its GPS coordinates. Several events at the same
/// place share a row, and typing coordinates sets them on all of those events.
/// </summary>
public sealed partial class PlaceRow : ObservableObject
{
    private readonly List<Event> _events;
    private readonly Action<Action> _apply;

    /// <param name="apply">Runs a change to the events as one undoable edit.</param>
    public PlaceRow(string place, List<Event> events, Action<Action> apply)
    {
        Place = place;
        _events = events;
        _apply = apply;
        EventsText = string.Join("  ·  ", events.Select(EventTypes.Label).Distinct());
        _coordinates = events.Select(e => e.Location).FirstOrDefault(l => l != null)?.ToString() ?? "";
    }

    public string Place { get; }
    public string EventsText { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError), nameof(MapUrl), nameof(HasLocation))]
    private string _coordinates;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string _error = "";

    public bool HasError => Error.Length > 0;
    public bool HasLocation => GeoPoint.Parse(Coordinates) != null;

    /// <summary>A marker at the coordinates, or a search for the place name when there are none yet.</summary>
    public string MapUrl => GeoPoint.Parse(Coordinates) is { } at
        ? at.OpenStreetMapUrl
        : Lineage.Core.Places.Gazetteer.MapUrl(Place);

    partial void OnCoordinatesChanged(string value)
    {
        GeoPoint? at = null;
        if (value.Trim().Length > 0)
        {
            at = GeoPoint.Parse(value);
            if (at == null) { Error = "Couldn't read that — try something like 42.2626, -71.8023"; return; }
        }
        Error = "";
        if (_events.All(e => e.Location == at)) return;
        _apply(() => { foreach (var e in _events) e.Location = at; });
    }
}
