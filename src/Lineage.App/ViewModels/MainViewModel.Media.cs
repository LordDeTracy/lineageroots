using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Media;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<PhotoTile> SourcePhotos { get; } = new();
    public bool HasSourcePhotos => SourcePhotos.Count > 0;

    /// <summary>The file a media link points at (relative links are relative to the tree file).</summary>
    internal string ResolveMedia(string file) => MediaLibrary.Resolve(FilePath, file);

    private PhotoTile Tile(MediaLink m) => new(m, ResolveMedia(m.File));

    /// <summary>The person's own photos, recordings and documents, then photos of other records they are tagged in.</summary>
    private void ShowPhotos(Person p)
    {
        Photos.Clear();
        ProfilePhoto = null;
        foreach (var m in p.Media.OrderByDescending(m => m.Primary).ThenBy(m => PhotoTile.IsPhoto(m) ? 0 : 1))
            Photos.Add(Tile(m));
        foreach (var r in MediaLibrary.TaggedIn(_tree, p.Id))
            if (!p.Media.Contains(r.Link) && PhotoTile.IsPhoto(r.Link)) Photos.Add(Tile(r.Link));
        foreach (var tile in Photos) _ = tile.LoadAsync(320);
        if (Photos.FirstOrDefault(t => t.IsProfile && p.Media.Contains(t.Media)) is { } profile) _ = LoadProfileAsync(p, profile);
    }

    private async Task LoadProfileAsync(Person p, PhotoTile tile)
    {
        await tile.LoadAsync(320);
        if (_current == p) ProfilePhoto = tile.Thumbnail;
    }

    private void RebuildPlaces(Person p)
    {
        Places.Clear();
        var families = p.SpouseFamilyIds.Select(id => _tree.Family(id)).OfType<Family>().ToList();
        foreach (var g in p.Events.Concat(families.SelectMany(f => f.Events)).Where(e => e.Place.Trim().Length > 0)
                     .GroupBy(e => e.Place.Trim(), StringComparer.OrdinalIgnoreCase))
            Places.Add(new PlaceRow(g.Key, g.ToList(), change =>
            {
                // Typing coordinates is an in-place edit: record it for undo without reloading the page,
                // so focus stays where the user is typing.
                var edit = _history.Begin("set GPS location", families.Cast<Entity>().Prepend(p));
                change();
                edit.Commit();
                MarkChanged();
                NotifyUndo();
                RebuildTimeline(p);
                if (IsMapView) RebuildPersonMap();
            }));
        HasPlaces = Places.Count > 0;
    }

    /// <summary>Picks photos and makes links for them (relative when they're already in the tree's folder).</summary>
    internal async Task<List<MediaLink>> PickPhotosAsync(string title, bool recordings = false)
    {
        var files = recordings
            ? await _dialogs.OpenFilesAsync(title, ("Photos, sound, video and documents", PhotoTile.AllTypes), ("Photos", PhotoTile.FileTypes),
                ("Sound recordings", Lineage.Core.Media.MediaKinds.SoundTypes), ("Videos", Lineage.Core.Media.MediaKinds.VideoTypes),
                ("Documents", Lineage.Core.Media.MediaKinds.DocumentTypes))
            : await _dialogs.OpenFilesAsync(title, ("Photos", PhotoTile.FileTypes));
        if (files.Count == 0) return new();
        var path = FilePath;
        return await Task.Run(() => files.Select(f =>
        {
            var link = PhotoTile.FromFile(f);
            link.File = MediaLibrary.ToLink(path, f);
            return link;
        }).ToList());
    }

    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task AddPhotosAsync()
    {
        if (_current is not { } p) return;
        var links = await PickPhotosAsync($"Add photos or recordings of {p.DisplayName}", recordings: true);
        var known = p.Media.Select(m => ResolveMedia(m.File)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        links = links.Where(l => known.Add(ResolveMedia(l.File))).ToList();
        if (links.Count == 0) return;
        Change(links.Count == 1 ? "add photo" : "add photos", new[] { p }, () =>
        {
            if (!p.Media.Any(m => m.Primary) && links.FirstOrDefault(PhotoTile.IsPhoto) is { } first) first.Primary = true;
            p.Media.AddRange(links);
        });
        var located = links.Count(l => l.Location != null);
        var pictures = links.Count(PhotoTile.IsPhoto);
        var others = links.Count - pictures;
        var added = new List<string>();
        if (pictures > 0) added.Add(pictures == 1 ? "1 photo" : $"{pictures} photos");
        if (others > 0) added.Add(others == 1 ? "1 recording or document" : $"{others} recordings or documents");
        Status = "Added " + string.Join(" and ", added) + (located > 0 ? $" — {located} with a GPS location from the camera" : "");
    }

    /// <summary>Opens the photo viewer (tag people, edit title, date and place) at this photo.</summary>
    [RelayCommand]
    private Task OpenPhotoAsync(PhotoTile tile)
    {
        // Recordings and documents open in the computer's own player or viewer.
        if (tile.IsFile) { OpenPhotoExternally(tile); return Task.CompletedTask; }
        return ViewPhotosAsync(Photos.Where(t => t.IsPicture).Select(t => t.Media).ToList(), tile.Media);
    }

    [RelayCommand]
    private Task OpenSourcePhotoAsync(PhotoTile tile)
    {
        if (tile.IsFile) { OpenPhotoExternally(tile); return Task.CompletedTask; }
        return ViewPhotosAsync(SourcePhotos.Where(t => t.IsPicture).Select(t => t.Media).ToList(), tile.Media);
    }

    private async Task ViewPhotosAsync(List<MediaLink> photos, MediaLink start)
    {
        var viewer = new PhotoViewerViewModel(_tree, photos, Math.Max(0, photos.IndexOf(start)), ResolveMedia);
        if (!await _dialogs.ShowPhotosAsync(viewer)) return;
        var edits = viewer.Results;
        if (edits.Count > 0)
            Change(edits.Count == 1 ? "edit photo" : "edit photos",
                   edits.Select(e => MediaLibrary.OwnerOf(_tree, e.Original)).OfType<Entity>(),
                   () => { foreach (var e in edits) e.Apply(); });
        if (viewer.OpenPersonId is { } id) OpenPerson(id);
    }

    /// <summary>The person showing and anyone else, side by side (split view).</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private async Task SideBySideAsync()
    {
        if (_current is not { } p) return;
        var view = new SideBySideViewModel(_tree, p);
        await _dialogs.ShowSideBySideAsync(view);
        if (view.OpenPersonId is { } id) OpenPerson(id);
    }

    /// <summary>Soundex, date and age calculators and calendar conversions.</summary>
    [RelayCommand]
    private Task OpenCalculatorsAsync() => _dialogs.ShowCalculatorsAsync(new CalculatorsViewModel(_tree));

    /// <summary>The person's photos (and those they're tagged in) as a full-screen slide show.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentPerson))]
    private Task SlideShowAsync() => ShowSlidesAsync(Photos.Where(t => t.IsPicture).Select(t => t.Media));

    /// <summary>Every photo in the tree as a slide show, for a family gathering.</summary>
    [RelayCommand]
    private Task SlideShowAllAsync() => ShowSlidesAsync(MediaLibrary.AllLinks(_tree).Where(r => r.Owner is not Source).Select(r => r.Link));

    private async Task ShowSlidesAsync(IEnumerable<MediaLink> media)
    {
        var slides = SlideShowViewModel.From(_tree, media, ResolveMedia);
        if (slides.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Slide show", "There are no photos to show yet — or their files can't be found.");
            return;
        }
        await _dialogs.ShowSlideShowAsync(new SlideShowViewModel(slides));
    }

    [RelayCommand]
    private void OpenPhotoExternally(PhotoTile tile)
    {
        if (!tile.IsMissing) OpenExternal(tile.FullPath);
    }

    [RelayCommand]
    private void SetProfilePhoto(PhotoTile tile)
    {
        if (_current is not { } p || !p.Media.Contains(tile.Media) || !tile.IsPicture) return;
        Change("change profile photo", new[] { p }, () =>
        {
            foreach (var m in p.Media) m.Primary = ReferenceEquals(m, tile.Media);
        });
    }

    [RelayCommand]
    private void RemovePhoto(PhotoTile tile)
    {
        if (_current is not { } p) return;
        if (p.Media.Contains(tile.Media))
        {
            Change("remove photo", new[] { p }, () =>
            {
                p.Media.Remove(tile.Media);
                if (tile.IsProfile && p.Media.FirstOrDefault(PhotoTile.IsPhoto) is { } next) next.Primary = true;
            });
            Status = "Photo removed from this person. The file itself was not touched.";
        }
        else if (MediaLibrary.OwnerOf(_tree, tile.Media) is { } owner)
        {
            // A photo of someone else that this person is tagged in: remove just the tag.
            Change("remove photo tag", new[] { owner }, () => tile.Media.Regions.RemoveAll(r => r.PersonId == p.Id));
            Status = $"{p.DisplayName} is no longer tagged in that photo.";
        }
    }

    /// <summary>Opens a map in the browser — only when the user clicks a Map button.</summary>
    [RelayCommand]
    private static void ShowOnMap(string url)
    {
        if (url.StartsWith("https://www.openstreetmap.org/", StringComparison.Ordinal)) OpenExternal(url);
    }

    // ---- keeping photos with the tree -----------------------------------------------------------

    /// <summary>Copies every photo into a "&lt;tree&gt; media" folder beside the tree file, so they travel together.</summary>
    [RelayCommand]
    private async Task CopyMediaIntoTreeFolderAsync()
    {
        if (!HasTree) return;
        if (FilePath is not { } path)
        {
            await _dialogs.ShowMessageAsync("Save the tree first", "Photos are copied into a folder next to the tree file, so save the tree somewhere first.");
            return;
        }
        var plan = await Task.Run(() => MediaLibrary.PlanCopy(_tree, path));
        if (plan.FilesToCopy == 0 && plan.LinksToUpdate == 0)
        {
            await _dialogs.ShowMessageAsync("Nothing to copy", plan.Missing.Count > 0
                ? $"All the photos that can be found are already with the tree. {plan.Missing.Count} can't be found — use Find missing photos."
                : "All the photos are already in the tree's folder.");
            return;
        }
        if (!await _dialogs.ConfirmAsync("Keep photos with the tree",
                $"Copy {plan.FilesToCopy:N0} file{(plan.FilesToCopy == 1 ? "" : "s")} into \"{plan.Folder}\"? Your originals stay where they are." +
                (plan.Missing.Count > 0 ? $" {plan.Missing.Count:N0} can't be found and will be left as they are." : "")))
            return;
        var edit = _history.Begin("keep photos with the tree", plan.Owners);
        try
        {
            Status = "Copying photos…";
            var copied = await Task.Run(() => plan.CopyFiles());
            plan.UpdateLinks();
            edit.Commit();
            AfterChange();
            Status = $"Copied {copied:N0} photo{(copied == 1 ? "" : "s")} into the tree's folder. Save to keep the new links.";
        }
        catch (Exception ex)
        {
            edit.Commit();
            AfterChange();
            await _dialogs.ShowMessageAsync("Could not copy the photos", ex.Message);
        }
    }

    /// <summary>Finds photos that were moved: searches a folder for files with the same names.</summary>
    [RelayCommand]
    private async Task FindMissingMediaAsync()
    {
        if (!HasTree) return;
        var missing = MediaLibrary.FindMissing(_tree, FilePath);
        if (missing.Count == 0)
        {
            await _dialogs.ShowMessageAsync("No missing photos", "Every photo in this tree can be found.");
            return;
        }
        var folder = await _dialogs.PickFolderAsync($"{missing.Count} photos can't be found — choose a folder to search");
        if (folder == null) return;
        var path = FilePath;
        Status = "Looking for the photos…";
        var plan = await Task.Run(() => MediaLibrary.PlanRelink(_tree, path, folder));
        var fixedCount = 0;
        if (plan.Found > 0) Change("find missing photos", plan.Owners, () => fixedCount = plan.Apply());
        Status = $"Found {fixedCount:N0} of {missing.Count:N0} missing photos." +
                 (plan.StillMissing.Count > 0 ? $" {plan.StillMissing.Count:N0} are still missing." : "");
    }

    // ---- source photos (scans of records) ------------------------------------------------------

    private void ShowSourcePhotos(Source? s)
    {
        SourcePhotos.Clear();
        if (s != null)
            foreach (var m in s.Media) SourcePhotos.Add(Tile(m));
        foreach (var t in SourcePhotos) _ = t.LoadAsync(320);
        OnPropertyChanged(nameof(HasSourcePhotos));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSource))]
    private async Task AddSourcePhotosAsync()
    {
        if (SelectedSource is not { } row || !_tree.Sources.TryGetValue(row.Id, out var s)) return;
        var links = await PickPhotosAsync($"Add images of {row.Title}");
        if (links.Count == 0) return;
        Change("add source images", new[] { s }, () => s.Media.AddRange(links));
    }

    [RelayCommand]
    private void RemoveSourcePhoto(PhotoTile tile)
    {
        if (SelectedSource is not { } row || !_tree.Sources.TryGetValue(row.Id, out var s)) return;
        Change("remove source image", new[] { s }, () => s.Media.Remove(tile.Media));
    }
}
