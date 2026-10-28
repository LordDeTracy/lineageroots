using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lineage.App.Controls;
using Lineage.App.ViewModels;
using Lineage.App.Views;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Media;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

/// <summary>The photo viewer: tagging people, editing the caption, moving between photos.</summary>
public sealed class PhotoViewerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"lineage-photos-{Guid.NewGuid():N}");

    public PhotoViewerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { /* a bitmap may still hold the file */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Draws a simple picture (two "faces" on a background) and saves it as a PNG.</summary>
    private string Picture(string name, Color background, int width = 480, int height = 320)
    {
        var path = Path.Combine(_folder, name);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(new SolidColorBrush(background), new Rect(0, 0, width, height));
            ctx.DrawEllipse(Brushes.Wheat, null, new Point(width * 0.3, height * 0.4), width * 0.07, height * 0.12);
            ctx.DrawEllipse(Brushes.Tan, null, new Point(width * 0.68, height * 0.42), width * 0.07, height * 0.12);
            ctx.FillRectangle(Brushes.DarkSlateGray, new Rect(0, height * 0.75, width, height * 0.25));
        }
        bitmap.Save(path);
        return path;
    }

    private (FamilyTree Tree, List<MediaLink> Photos) Setup()
    {
        var tree = GedcomImporter.Import(Demo.Gedcom);
        var photos = new List<MediaLink>
        {
            new() { File = Picture("forge.png", Colors.SteelBlue), Title = "At the forge", MediaType = "PHOTO", Taken = "1 JUN 1925" },
            new()
            {
                File = Picture("wedding.png", Colors.RosyBrown), Title = "Wedding", MediaType = "PHOTO",
                Regions = { new PhotoRegion { PersonId = 4, X = 0.6, Y = 0.25, Width = 0.15, Height = 0.3 } },
            },
            new() { File = Path.Combine(_folder, "moved away.jpg"), Title = "Lost", MediaType = "PHOTO" },
        };
        return (tree, photos);
    }

    private static PhotoViewerViewModel Viewer(FamilyTree tree, List<MediaLink> photos, int start = 0) =>
        new(tree, photos, start, file => file);

    [AvaloniaFact]
    public async Task ShowsThePhotoAndExplainsAMissingOne()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos);
        await vm.ImageTask;
        Assert.NotNull(vm.Image);
        Assert.False(vm.HasMessage);
        Assert.Equal("Photo 1 of 3", vm.Position);

        vm.Index = 2;
        await vm.ImageTask;
        Assert.Null(vm.Image);
        Assert.Equal("Photo not found", vm.MessageTitle);
        Assert.Contains("moved away.jpg", vm.MessageDetail);
    }

    [AvaloniaFact]
    public void TaggingWorksOnACopyUntilTheResultIsApplied()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos);
        vm.StartTaggingCommand.Execute(null);
        Assert.True(vm.IsDrawing);
        vm.PendingRegion = new Rect(0.2, 0.25, 0.2, 0.3);
        vm.PendingPerson = vm.People.Single(p => p.Id == 5);
        vm.ConfirmTagCommand.Execute(null);

        var tag = Assert.Single(vm.Tags);
        Assert.Equal("Clara Whitfield", tag.Name);
        Assert.Equal(new Rect(0.2, 0.25, 0.2, 0.3), tag.Bounds);
        Assert.True(vm.IsTagging);                  // ready for the next person
        Assert.False(vm.HasPendingRegion);
        Assert.Empty(photos[0].Regions);            // the tree's link is untouched

        var edit = Assert.Single(vm.Results);
        Assert.Same(photos[0], edit.Original);
        edit.Apply();
        Assert.Equal(5, Assert.Single(photos[0].Regions).PersonId);
        Assert.Empty(vm.Results);                   // nothing left to save
    }

    [AvaloniaFact]
    public void RemovingTagsAndResultsListOnlyChangedPhotos()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos);
        vm.AddTag(1, new Rect(0.1, 0.1, 0.2, 0.2));
        vm.RemoveTagCommand.Execute(vm.Tags.Single());
        Assert.Empty(vm.Results);                   // added and removed again: no change

        vm.NextCommand.Execute(null);
        var eliza = Assert.Single(vm.Tags);
        Assert.Equal("Eliza Hartley", eliza.Name);
        vm.RemoveTagCommand.Execute(eliza);
        Assert.True(vm.ShowEmpty);
        var edit = Assert.Single(vm.Results);
        Assert.Same(photos[1], edit.Original);
        Assert.Empty(edit.Edited.Regions);
        Assert.Single(photos[1].Regions);
    }

    [AvaloniaFact]
    public void ATypedNameIsEnoughWhenItNamesOnePerson()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos);
        vm.StartTaggingCommand.Execute(null);

        vm.PendingText = "Clara";
        vm.ConfirmTagCommand.Execute(null);
        Assert.True(vm.HasTagError);                // no box drawn yet

        vm.PendingRegion = new Rect(0.1, 0.1, 0.2, 0.2);
        vm.PendingText = "Thomas";                  // two people called Thomas
        vm.ConfirmTagCommand.Execute(null);
        Assert.True(vm.HasTagError);
        Assert.Empty(vm.Tags);

        vm.PendingText = "thomas whitfeld";
        vm.ConfirmTagCommand.Execute(null);
        Assert.Equal(6, Assert.Single(vm.Tags).PersonId);

        vm.PendingRegion = new Rect(0.5, 0.1, 0.2, 0.2);
        vm.PendingText = "clara";
        vm.ConfirmTagCommand.Execute(null);
        Assert.Equal(new long[] { 6, 5 }, vm.Tags.Select(t => t.PersonId));   // left to right
    }

    [AvaloniaFact]
    public void TaggingSomeoneAgainMovesTheirBox()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos, start: 1);
        vm.AddTag(4, new Rect(0.1, 0.2, 0.123456, 0.3));
        var tag = Assert.Single(vm.Current.Regions);
        Assert.Equal((0.1, 0.1235), (tag.X, tag.Width));   // kept to the 4 decimals GEDCOM stores
    }

    [AvaloniaFact]
    public void NavigatingKeepsEachPhotosEdits()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos);
        Assert.False(vm.PreviousCommand.CanExecute(null));
        Assert.Equal("1 Jun 1925", vm.Taken);

        vm.Taken = "1 Jun 1925";                    // retyped as shown: not a change
        Assert.Empty(vm.Results);
        vm.Title = "Thomas at the forge ";
        vm.Taken = "abt 1926";
        vm.Location = "not a place";
        Assert.True(vm.HasLocationError);
        Assert.Null(vm.Current.Location);
        vm.Location = "53.8008, -1.5491";

        vm.NextCommand.Execute(null);
        Assert.Equal("Wedding", vm.Title);
        Assert.Equal("", vm.Taken);
        vm.NextCommand.Execute(null);
        Assert.False(vm.NextCommand.CanExecute(null));
        vm.Index = 0;
        Assert.Equal("Thomas at the forge", vm.Title);
        Assert.Equal("about 1926", vm.Taken);

        var edit = Assert.Single(vm.Results);
        Assert.Same(photos[0], edit.Original);
        Assert.Equal(("Thomas at the forge", "ABT 1926"), (edit.Edited.Title, edit.Edited.Taken));
        Assert.Equal(new GeoPoint(53.8008, -1.5491), edit.Edited.Location);
        Assert.Equal("At the forge", photos[0].Title);
    }

    [AvaloniaFact]
    public void ClickingATaggedNameAsksToOpenThatPerson()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos, start: 1);
        long? asked = null;
        vm.OpenPersonRequested += id => asked = id;
        vm.OpenPersonCommand.Execute(vm.Tags.Single());
        Assert.Equal(4, asked);
        Assert.Equal(4, vm.OpenPersonId);
    }

    [AvaloniaFact]
    public async Task WindowDrawsAndTagsByDraggingAndEscapeStopsTaggingFirst()
    {
        var (tree, photos) = Setup();
        var vm = Viewer(tree, photos, start: 1);
        var window = new PhotoViewerWindow { DataContext = vm };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        vm.Index = 2;                               // the missing file shows a message, not an error
        await vm.ImageTask;
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.HasMessage);
        Assert.False(vm.IsLoading);                 // the load of the previous photo was abandoned
        Snapshot(window, "photo-viewer-missing.png");
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Equal(1, vm.Index);
        await vm.ImageTask;
        Dispatcher.UIThread.RunJobs();
        Snapshot(window, "photo-viewer.png");

        var surface = window.GetVisualDescendants().OfType<PhotoTagSurface>().Single();
        var image = surface.ImageRect;
        Assert.True(image.Width > 300);
        Point At(double x, double y) => surface.TranslatePoint(new Point(image.X + x * image.Width, image.Y + y * image.Height), window)!.Value;
        window.MouseMove(At(0.67, 0.4));            // over Eliza's box: her row lights up
        Assert.Equal(4, vm.HighlightedTag?.PersonId);
        Assert.True(vm.Tags.Single().IsHighlighted);
        window.MouseMove(At(0.1, 0.9));
        Assert.Null(vm.HighlightedTag);

        vm.StartTaggingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.MouseDown(At(0.2, 0.25), MouseButton.Left);
        window.MouseMove(At(0.3, 0.4));
        window.MouseMove(At(0.4, 0.55));
        window.MouseUp(At(0.4, 0.55), MouseButton.Left);
        var box = Assert.IsType<Rect>(vm.PendingRegion);
        Assert.Equal(0.2, box.X, 2);
        Assert.Equal(0.3, box.Height, 2);

        vm.PendingPerson = vm.People.Single(p => p.Id == 5);
        Dispatcher.UIThread.RunJobs();
        Snapshot(window, "photo-viewer-who.png");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(new long[] { 5, 4 }, vm.Tags.Select(t => t.PersonId));
        vm.HighlightedTag = vm.Tags[0];
        Dispatcher.UIThread.RunJobs();
        Snapshot(window, "photo-viewer-tagging.png");

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(vm.IsTagging);
        Assert.False(closed);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.True(closed);
        Assert.Null(vm.Image);                      // the decoded photo is released on close
    }

    /// <summary>Saves what the window looks like when LINEAGE_SCREENSHOTS names a folder (for reviewing the design).</summary>
    private static void Snapshot(Avalonia.Controls.Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LINEAGE_SCREENSHOTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public async Task FacesFoundCanBeTaggedWithAClick()
    {
        var (tree, photos) = Setup();
        using var vm = Viewer(tree, photos, 1);   // the wedding, with Eliza tagged on the right
        await vm.ImageTask;
        await vm.FaceTask;
        vm.ShowFaces(new() { new FaceBox(0.25, 0.3, 0.1, 0.15, 0.99), new FaceBox(0.62, 0.3, 0.1, 0.15, 0.98) });
        var face = Assert.Single(vm.FaceBoxes);   // the other is Eliza's face, already tagged
        Assert.Equal(new Rect(0.25, 0.3, 0.1, 0.15), face);
        Assert.StartsWith("1 face found that isn't tagged yet", vm.FaceHint);

        vm.TagFaceCommand.Execute(face);
        Assert.True(vm.IsTagging);
        var box = vm.PendingRegion!.Value;   // a little larger than the face
        Assert.Equal((0.235, 0.282, 0.13, 0.186), (Math.Round(box.X, 3), Math.Round(box.Y, 3), Math.Round(box.Width, 3), Math.Round(box.Height, 3)));
        vm.PendingPerson = vm.People.Single(p => p.Id == 1);
        vm.ConfirmTagCommand.Execute(null);
        Assert.Equal(2, vm.Tags.Count);
        Assert.False(vm.HasFaceBoxes);
        Assert.Equal("", vm.FaceHint);
    }
}
