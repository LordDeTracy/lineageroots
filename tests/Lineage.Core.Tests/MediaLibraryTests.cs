using Lineage.Core.Media;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

/// <summary>Keeping photos with the tree: finding, copying and relinking files in a temp folder.</summary>
public sealed class MediaLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lineage-media-{Guid.NewGuid():N}");

    public MediaLibraryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* a scanner still has a file open */ }
    }

    private string TreePath => Path.Combine(_root, "trees", "Smith.lineage");
    private string MediaFolder => Path.Combine(_root, "trees", "Smith media");

    private string At(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    private string Write(string relative, string content)
    {
        var path = At(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static Person Person(FamilyTree t, string given) =>
        t.AddPerson(new Person { Names = { new Name { Given = given, Surname = "Smith" } } });

    private sealed class Percentages : IProgress<int>
    {
        public List<int> Seen { get; } = new();
        public void Report(int value) => Seen.Add(value);
    }

    [Fact]
    public void AllLinksNamesTheOwnerOfEveryLink()
    {
        var t = new FamilyTree();
        var ann = Person(t, "Ann");
        var bob = Person(t, "Bob");
        ann.Media.Add(new MediaLink { File = "ann.jpg", Regions = { new PhotoRegion { PersonId = bob.Id } } });
        ann.Events.Add(new Event { Tag = "GRAD", Media = { new MediaLink { File = "diploma.jpg" } } });
        var fam = t.AddFamily(new Family());
        t.SetPartner(fam, ann, second: false);
        fam.Media.Add(new MediaLink { File = "couple.jpg" });
        fam.Events.Add(new Event { Tag = "MARR", Media = { new MediaLink { File = "wedding.jpg", Regions = { new PhotoRegion { PersonId = bob.Id } } } } });
        fam.Children.Add(new ChildLink { ChildId = bob.Id, Sealing = new Event { Tag = "SLGC", Media = { new MediaLink { File = "sealing.jpg" } } } });
        var src = t.AddSource(new Source { Title = "Letters", Media = { new MediaLink { File = "letter.jpg" } } });

        var links = MediaLibrary.AllLinks(t).ToList();
        Assert.Equal(new[] { "ann.jpg", "diploma.jpg", "couple.jpg", "wedding.jpg", "sealing.jpg", "letter.jpg" }, links.Select(r => r.Link.File));
        Assert.Equal(new Entity[] { ann, ann, fam, fam, fam, src }, links.Select(r => r.Owner));
        Assert.Equal(new[] { null, "GRAD", null, "MARR", "SLGC", null }, links.Select(r => r.Event?.Tag));

        Assert.Same(fam, MediaLibrary.OwnerOf(t, fam.Events[0].Media[0]));
        Assert.Null(MediaLibrary.OwnerOf(t, new MediaLink { File = "ann.jpg" }));
        Assert.Equal(new[] { "ann.jpg", "wedding.jpg" }, MediaLibrary.TaggedIn(t, bob.Id).Select(r => r.Link.File));
    }

    [Fact]
    public void ResolveReadsRelativeLinksFromTheTreeFolder()
    {
        var expected = Path.Combine(MediaFolder, "a.jpg");
        Assert.Equal(expected, MediaLibrary.Resolve(TreePath, "Smith media/a.jpg"));
        Assert.Equal(expected, MediaLibrary.Resolve(TreePath, @"Smith media\a.jpg"));
        Assert.Equal(Path.Combine(_root, "photos", "b.jpg"), MediaLibrary.Resolve(TreePath, "../photos/b.jpg"));
        var absolute = At("photos/c.jpg");
        Assert.Equal(absolute, MediaLibrary.Resolve(TreePath, absolute));
        Assert.Equal("Smith media/a.jpg", MediaLibrary.Resolve(null, "Smith media/a.jpg"));
        Assert.Equal("https://example.org/a.jpg", MediaLibrary.Resolve(TreePath, "https://example.org/a.jpg"));
        Assert.Equal("Smith media/a.jpg", MediaLibrary.ToLink(TreePath, expected));
        Assert.Equal(absolute, MediaLibrary.ToLink(TreePath, absolute));
    }

    [Fact]
    public void CopyIntoTreeFolderCopiesEachFileOnceAndPointsLinksAtTheCopies()
    {
        var a = Write("photos/a.jpg", "AAAA");
        var copyOfA = Write("scans/copy of a.jpg", "AAAA");      // same content, other name
        var otherA = Write("other/a.jpg", "BBBB");               // same name, other content
        Write("trees/Smith media/kept.jpg", "KKKK");            // already in the folder
        var keptElsewhere = Write("elsewhere/kept again.jpg", "KKKK");
        var lost = At("gone/lost.jpg");

        var t = new FamilyTree();
        var ann = Person(t, "Ann");
        ann.Media.Add(new MediaLink { File = a });
        ann.Media.Add(new MediaLink { File = copyOfA });
        ann.Events.Add(new Event { Tag = "BIRT", Media = { new MediaLink { File = otherA } } });
        var fam = t.AddFamily(new Family { Media = { new MediaLink { File = "Smith media/kept.jpg" } } });
        var src = t.AddSource(new Source { Media = { new MediaLink { File = keptElsewhere } } });
        var bob = Person(t, "Bob");
        bob.Media.Add(new MediaLink { File = lost });
        bob.Media.Add(new MediaLink { File = "https://example.org/bob.jpg" });

        var plan = MediaLibrary.PlanCopy(t, TreePath);
        Assert.Equal(2, plan.FilesToCopy);
        Assert.Equal(4, plan.LinksToUpdate);
        Assert.Equal(new Entity[] { ann, src }, plan.Owners);   // what to snapshot for undo
        Assert.False(File.Exists(Path.Combine(MediaFolder, "a.jpg")));   // planning changes nothing
        Assert.Equal(a, ann.Media[0].File);

        var progress = new Percentages();
        var result = plan.Apply(progress);
        Assert.Equal((2, 1, 4), (result.Copied, result.AlreadyThere, result.LinksUpdated));
        Assert.Equal(new[] { lost }, result.Missing);
        Assert.Equal(100, progress.Seen[^1]);

        Assert.Equal("Smith media/a.jpg", ann.Media[0].File);
        Assert.Equal("Smith media/a.jpg", ann.Media[1].File);
        Assert.Equal("Smith media/a (2).jpg", ann.Events[0].Media[0].File);
        Assert.Equal("Smith media/kept.jpg", fam.Media[0].File);
        Assert.Equal("Smith media/kept.jpg", src.Media[0].File);
        Assert.Equal(lost, bob.Media[0].File);
        Assert.Equal(4, ann.Media[0].Size);
        Assert.Equal("AAAA", File.ReadAllText(Path.Combine(MediaFolder, "a.jpg")));
        Assert.Equal("BBBB", File.ReadAllText(Path.Combine(MediaFolder, "a (2).jpg")));
        Assert.Equal(3, Directory.GetFiles(MediaFolder).Length);
        Assert.True(File.Exists(a));   // originals are left where they were

        var again = MediaLibrary.CopyIntoTreeFolder(t, TreePath);
        Assert.Equal((0, 3, 0), (again.Copied, again.AlreadyThere, again.LinksUpdated));
        Assert.Empty(again.Owners);
    }

    [Fact]
    public void CopyCanRunInTwoStepsForABackgroundThread()
    {
        var a = Write("photos/a.jpg", "AAAA");
        var t = new FamilyTree();
        var ann = Person(t, "Ann");
        ann.Media.Add(new MediaLink { File = a });

        var plan = MediaLibrary.PlanCopy(t, TreePath);
        Assert.Equal(1, plan.CopyFiles());
        Assert.Equal(a, ann.Media[0].File);                 // the tree is untouched until UpdateLinks
        Assert.Equal(0, plan.CopyFiles());                  // copying twice is harmless
        Assert.Equal(1, plan.UpdateLinks());
        Assert.Equal("Smith media/a.jpg", ann.Media[0].File);
        Assert.True(File.Exists(MediaLibrary.Resolve(TreePath, ann.Media[0].File)));
    }

    [Fact]
    public void RelinkFindsMovedFilesByNameSizeAndFolder()
    {
        Write("trees/Smith media/here.jpg", "HERE");
        var t = new FamilyTree();
        var p1 = Person(t, "Ann");
        p1.Media.Add(new MediaLink { File = At("old/Pictures/2019/Trip/IMG_0001.jpg") });
        var p2 = Person(t, "Bob");
        p2.Media.Add(new MediaLink { File = At("old/x/portrait.png"), Size = 5 });
        var p3 = Person(t, "Cy");
        p3.Media.Add(new MediaLink { File = "Smith media/gone.jpg" });
        var p4 = Person(t, "Di");
        p4.Media.Add(new MediaLink { File = "Smith media/here.jpg" });

        var trip = Write("new/Pictures/2019/Trip/IMG_0001.jpg", "trip");
        Write("new/Pictures/2021/IMG_0001.jpg", "other trip");
        var portrait = Write("new/b/portrait.png", "12345");
        Write("new/a/portrait.png", "123");
        Write("trees/moved/gone.jpg", "gone");

        var missing = MediaLibrary.FindMissing(t, TreePath);
        Assert.Equal(new Entity[] { p1, p2, p3 }, missing.Select(m => m.Owner));

        var plan = MediaLibrary.PlanRelink(t, TreePath, _root);
        Assert.Equal(3, plan.Found);
        Assert.Equal(new Entity[] { p1, p2, p3 }, plan.Owners);
        Assert.Empty(plan.StillMissing);
        Assert.Equal(3, plan.Apply());

        Assert.Equal(trip, p1.Media[0].File);
        Assert.Equal(4, p1.Media[0].Size);
        Assert.Equal(portrait, p2.Media[0].File);
        Assert.Equal("moved/gone.jpg", p3.Media[0].File);    // inside the tree folder: kept relative
        Assert.Empty(MediaLibrary.FindMissing(t, TreePath));
    }

    [Fact]
    public void RelinkDoesNotGuessBetweenDifferentFiles()
    {
        var t = new FamilyTree();
        var ann = Person(t, "Ann");
        var lost = At("lost/photo.jpg");
        ann.Media.Add(new MediaLink { File = lost });
        ann.Media.Add(new MediaLink { File = At("lost/sized.jpg"), Size = 99 });
        Write("x/photo.jpg", "aaa");
        Write("y/photo.jpg", "bbbbb");
        Write("x/sized.jpg", "wrong size");

        Assert.Equal(0, MediaLibrary.Relink(t, TreePath, _root));
        Assert.Equal(lost, ann.Media[0].File);
        Assert.Equal(2, MediaLibrary.PlanRelink(t, TreePath, _root).StillMissing.Count);
    }

    [Fact]
    public void RetagMovesOrRemovesAPersonsTags()
    {
        var t = new FamilyTree();
        var ann = Person(t, "Ann");
        var photo = new MediaLink { File = "group.jpg", Regions = { new PhotoRegion { PersonId = 1 }, new PhotoRegion { PersonId = 2 } } };
        ann.Media.Add(photo);

        Assert.Equal(1, MediaLibrary.Retag(t, 1, 3));
        Assert.Equal(new long[] { 3, 2 }, photo.Regions.Select(r => r.PersonId));
        Assert.Equal(1, MediaLibrary.Retag(t, 2, 3));        // 3 is already tagged: the duplicate goes
        Assert.Equal(new long[] { 3 }, photo.Regions.Select(r => r.PersonId));
        Assert.Equal(1, MediaLibrary.Retag(t, 3, null));
        Assert.Empty(photo.Regions);
    }

    [Fact]
    public void RebaseKeepsRelativeLinksPointingAtTheSameFiles()
    {
        var t = new FamilyTree();
        var ann = Person(t, "Ann");
        var absolute = At("photos/c.jpg");
        ann.Media.Add(new MediaLink { File = "Smith media/a.jpg" });
        ann.Media.Add(new MediaLink { File = absolute });

        Assert.Empty(MediaLibrary.Rebase(t, TreePath, Path.Combine(_root, "trees", "Smith copy.lineage")));
        Assert.Equal("Smith media/a.jpg", ann.Media[0].File);

        Assert.Equal(new Entity[] { ann }, MediaLibrary.Rebase(t, TreePath, At("archive/Smith.lineage")));
        Assert.Equal(Path.Combine(MediaFolder, "a.jpg"), ann.Media[0].File);
        Assert.Equal(absolute, ann.Media[1].File);
    }
}
