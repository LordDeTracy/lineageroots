using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Media;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>One picture in a slide show, with the words shown under it.</summary>
public sealed record Slide(string Path, string Title, string Detail);

/// <summary>
/// Photos one after another, full screen, for a family gathering or a quiet look back: each with its title, who is in it
/// and when it was taken. It moves on by itself and can be paused, stepped and closed from the keyboard.
/// </summary>
public sealed partial class SlideShowViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;

    public SlideShowViewModel(IReadOnlyList<Slide> slides, double seconds = 6)
    {
        Slides = slides;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _timer.Tick += (_, _) => Next();
        Show(0);
    }

    /// <summary>Every photo in a list of media links that can be found on disk, with names for the people tagged in it.</summary>
    public static List<Slide> From(FamilyTree tree, IEnumerable<MediaLink> media, Func<string, string> resolve) =>
        media.Where(m => MediaKinds.Of(m) == MediaKind.Photo).Distinct()
            .Select(m => (Link: m, Path: resolve(m.File))).Where(x => File.Exists(x.Path))
            .Select(x =>
            {
                var people = x.Link.Regions.Select(r => tree.Person(r.PersonId)?.DisplayName).OfType<string>().Distinct().ToList();
                var taken = x.Link.Taken.Length > 0 ? Display.Date(GenDate.Parse(x.Link.Taken)) : "";
                var title = x.Link.Title.Length > 0 ? x.Link.Title : System.IO.Path.GetFileNameWithoutExtension(x.Link.File);
                return new Slide(x.Path, title, Display.Join(people.Count > 0 ? string.Join(", ", people) : "", taken));
            }).ToList();

    public IReadOnlyList<Slide> Slides { get; }
    public bool IsEmpty => Slides.Count == 0;

    [ObservableProperty] private int _index;
    [ObservableProperty] private Bitmap? _picture;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _position = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlayText))] private bool _playing;
    public string PlayText => Playing ? "Pause" : "Play";

    public void Start()
    {
        Playing = Slides.Count > 1;
        if (Playing) _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Playing = false;
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (Playing) Stop(); else Start();
    }

    [RelayCommand]
    public void Next() => Show(Index + 1);

    [RelayCommand]
    public void Previous() => Show(Index - 1);

    private void Show(int index)
    {
        if (Slides.Count == 0) { Title = "No photos to show"; return; }
        Index = (index % Slides.Count + Slides.Count) % Slides.Count;
        var slide = Slides[Index];
        Title = slide.Title;
        Detail = slide.Detail;
        Position = $"{Index + 1} of {Slides.Count}";
        if (Playing) { _timer.Stop(); _timer.Start(); }   // a step by hand gets the slide its full time
        var old = Picture;
        try
        {
            using var stream = File.OpenRead(slide.Path);
            Picture = Bitmap.DecodeToWidth(stream, 1920, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception) { Picture = null; }   // unreadable picture: its title still shows
        old?.Dispose();
    }
}
