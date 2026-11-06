using Lineage.Core.Model;

namespace Lineage.Core.Media;

/// <summary>What sort of thing a media link is.</summary>
public enum MediaKind { Photo, Sound, Video, Document, Other }

/// <summary>Tells photos, sound recordings, videos and documents apart, by the link's type or its file's extension.</summary>
public static class MediaKinds
{
    public const string PhotoTypes = "*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff";
    public const string SoundTypes = "*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.oga;*.flac;*.wma;*.opus";
    public const string VideoTypes = "*.mp4;*.m4v;*.mov;*.avi;*.mkv;*.wmv;*.webm;*.mpg;*.mpeg;*.3gp";
    public const string DocumentTypes = "*.pdf;*.doc;*.docx;*.odt;*.rtf;*.txt";

    private static HashSet<string> Set(string types) =>
        new(types.Split(';').Select(p => p.TrimStart('*')), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Photos = Set(PhotoTypes), Sounds = Set(SoundTypes), Videos = Set(VideoTypes), Documents = Set(DocumentTypes);

    public static MediaKind Of(string file)
    {
        var ext = Path.GetExtension(file);
        return Photos.Contains(ext) ? MediaKind.Photo : Sounds.Contains(ext) ? MediaKind.Sound
             : Videos.Contains(ext) ? MediaKind.Video : Documents.Contains(ext) ? MediaKind.Document : MediaKind.Other;
    }

    /// <summary>The kind, trusting the file's extension first and the GEDCOM type (PHOTO, AUDIO…) when that says nothing.</summary>
    public static MediaKind Of(MediaLink m)
    {
        var byFile = Of(m.File);
        if (byFile != MediaKind.Other) return byFile;
        return m.MediaType.ToUpperInvariant() switch
        {
            "PHOTO" or "PHOTOGRAPH" or "IMAGE" => MediaKind.Photo,
            "SOUND" or "AUDIO" => MediaKind.Sound,
            "VIDEO" => MediaKind.Video,
            "DOCUMENT" or "BOOK" or "CERTIFICATE" or "NEWSPAPER" => MediaKind.Document,
            _ => MediaKind.Other,
        };
    }

    /// <summary>The GEDCOM media type to store for a file.</summary>
    public static string GedcomType(MediaKind kind) => kind switch
    {
        MediaKind.Photo => "PHOTO",
        MediaKind.Sound => "AUDIO",
        MediaKind.Video => "VIDEO",
        MediaKind.Document => "DOCUMENT",
        _ => "OTHER",
    };
}
