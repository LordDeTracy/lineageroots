using System.Security.Cryptography;
using Lineage.Core.Model;

namespace Lineage.Core.Media;

/// <summary>
/// One media link and the record that owns it, which is the record an undo snapshot must cover.
/// <see cref="Event"/> is set when the link belongs to one of the owner's events.
/// </summary>
public sealed record MediaRef(MediaLink Link, Entity Owner, Event? Event = null);

/// <summary>What copying media into the tree's folder did.</summary>
/// <param name="Copied">Files copied into the folder.</param>
/// <param name="AlreadyThere">Files that were already in the folder (by location or identical content).</param>
/// <param name="LinksUpdated">Links rewritten to point into the folder.</param>
/// <param name="Missing">Linked files that don't exist, as written in the links.</param>
/// <param name="Owners">Records whose links changed.</param>
public sealed record MediaCopyResult(int Copied, int AlreadyThere, int LinksUpdated,
                                     IReadOnlyList<string> Missing, IReadOnlyList<Entity> Owners);

/// <summary>
/// Keeps a tree's photos and documents together with the tree file. A link is either a full path
/// or a path relative to the tree file's folder, so a tree and its "&lt;name&gt; media" folder can be
/// moved, backed up or shared as one unit. Operations that rewrite links come as plans: the caller
/// snapshots the plan's <c>Owners</c> for undo, then applies it.
/// </summary>
public static class MediaLibrary
{
    /// <summary>File names compare the way the file system does.</summary>
    internal static readonly StringComparer PathComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>"Smith media" for "Smith.lineage".</summary>
    public static string FolderName(string treeFilePath) => Path.GetFileNameWithoutExtension(treeFilePath) + " media";

    /// <summary>The full path of the media folder that sits next to the tree file.</summary>
    public static string FolderPath(string treeFilePath) => Path.Combine(TreeFolder(treeFilePath), FolderName(treeFilePath));

    private static string TreeFolder(string treeFilePath) =>
        Path.GetDirectoryName(Path.GetFullPath(treeFilePath)) ?? Path.GetFullPath(".");

    /// <summary>Every media link in the tree with its owner: people, families, their events (including sealings) and sources.</summary>
    public static IEnumerable<MediaRef> AllLinks(FamilyTree tree)
    {
        foreach (var p in tree.People.Values.OrderBy(p => p.Id))
        {
            foreach (var m in p.Media) yield return new(m, p);
            foreach (var e in p.Events)
                foreach (var m in e.Media) yield return new(m, p, e);
        }
        foreach (var f in tree.Families.Values.OrderBy(f => f.Id))
        {
            foreach (var m in f.Media) yield return new(m, f);
            foreach (var e in f.Events.Concat(f.Children.Select(c => c.Sealing).OfType<Event>()))
                foreach (var m in e.Media) yield return new(m, f, e);
        }
        foreach (var s in tree.Sources.Values.OrderBy(s => s.Id))
            foreach (var m in s.Media) yield return new(m, s);
    }

    /// <summary>The record holding this exact link object, or null if it isn't in the tree.</summary>
    public static Entity? OwnerOf(FamilyTree tree, MediaLink link) =>
        AllLinks(tree).FirstOrDefault(r => ReferenceEquals(r.Link, link))?.Owner;

    /// <summary>Photos anywhere in the tree in which the person is tagged.</summary>
    public static IEnumerable<MediaRef> TaggedIn(FamilyTree tree, long personId) =>
        AllLinks(tree).Where(r => r.Link.Regions.Any(g => g.PersonId == personId));

    /// <summary>
    /// Moves a person's photo tags to another person (after a merge), or removes them when
    /// <paramref name="toId"/> is null (after a delete). Snapshot the owners from
    /// <see cref="TaggedIn"/> first. Returns how many tags changed.
    /// </summary>
    public static int Retag(FamilyTree tree, long fromId, long? toId)
    {
        var changed = 0;
        foreach (var r in TaggedIn(tree, fromId).ToList())
        {
            var regions = r.Link.Regions;
            foreach (var g in regions.Where(g => g.PersonId == fromId).ToList())
            {
                if (toId is { } to && regions.All(x => x.PersonId != to)) g.PersonId = to;
                else regions.Remove(g);   // deleted, or the other person is already tagged in this photo
                changed++;
            }
        }
        return changed;
    }

    /// <summary>Web addresses are links too, but never files to copy or look for.</summary>
    public static bool IsRemote(string file) =>
        file.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || file.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The full path of a linked file. Relative links ("Smith media/a.jpg", either slash) are
    /// relative to the tree file's folder; without a tree file they are returned unchanged.
    /// </summary>
    public static string Resolve(string? treeFilePath, string file)
    {
        var f = file.Trim();
        if (f.Length == 0 || IsRemote(f)) return file;
        if (f.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(f, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;
        if (Path.IsPathFullyQualified(f) || string.IsNullOrEmpty(treeFilePath)) return f;
        var relative = f.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(relative, TreeFolder(treeFilePath));
    }

    /// <summary>How a link to a file is written: relative with forward slashes when it is inside the tree's folder, else the full path.</summary>
    public static string ToLink(string? treeFilePath, string fullPath)
    {
        if (string.IsNullOrEmpty(treeFilePath)) return fullPath;
        var folder = TreeFolder(treeFilePath);
        return IsUnder(fullPath, folder) ? Path.GetRelativePath(folder, fullPath).Replace('\\', '/') : fullPath;
    }

    private static bool IsUnder(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, PathComparison);

    /// <summary>Links whose file isn't there (web addresses and empty links are not counted).</summary>
    public static IReadOnlyList<MediaRef> FindMissing(FamilyTree tree, string? treeFilePath) =>
        AllLinks(tree).Where(r => r.Link.File.Trim().Length > 0 && !IsRemote(r.Link.File)
                                  && !File.Exists(Resolve(treeFilePath, r.Link.File))).ToList();

    /// <summary>
    /// Copies every linked file that exists into the media folder next to the tree file and points
    /// the links at the copies. Identical files (same SHA-256) are kept once, files with the same
    /// name but different content get "name (2).jpg", and files already in the folder stay put.
    /// For undo, use <see cref="PlanCopy"/> and snapshot its owners before applying.
    /// </summary>
    /// <param name="progress">Percent of files copied, 0–100.</param>
    public static MediaCopyResult CopyIntoTreeFolder(FamilyTree tree, string treeFilePath, IProgress<int>? progress = null) =>
        PlanCopy(tree, treeFilePath).Apply(progress);

    /// <summary>Works out what <see cref="CopyIntoTreeFolder"/> would do without changing anything (reads and hashes files).</summary>
    public static MediaCopyPlan PlanCopy(FamilyTree tree, string treeFilePath)
    {
        var folder = FolderPath(treeFilePath);
        var hashes = new ContentHashes();
        var existing = (Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles().ToList() : new List<FileInfo>())
            .ToLookup(f => f.Length, f => f.FullName);
        var names = new HashSet<string>(existing.SelectMany(g => g).Select(Path.GetFileName).OfType<string>(), PathComparer);
        var copies = new List<(string From, string To, long Size)>();
        var targetOf = new Dictionary<string, (string Path, long Size)>(PathComparer);
        var inFolder = new HashSet<string>(PathComparer);
        var missing = new List<string>();
        var rewrites = new List<(MediaLink Link, string File, long Size)>();
        var owners = new Owners();

        foreach (var r in AllLinks(tree))
        {
            var file = r.Link.File;
            if (file.Trim().Length == 0 || IsRemote(file)) continue;
            var full = Resolve(treeFilePath, file);
            if (!File.Exists(full))
            {
                if (!missing.Contains(file, PathComparer)) missing.Add(file);
                continue;
            }
            if (!targetOf.TryGetValue(full, out var target)) targetOf[full] = target = Target(full);
            var link = ToLink(treeFilePath, target.Path);
            if (link == file) continue;
            rewrites.Add((r.Link, link, target.Size));
            owners.Add(r.Owner);
        }
        return new MediaCopyPlan(folder, copies.Select(c => (c.From, c.To)).ToList(), rewrites, owners.List, missing, inFolder.Count);

        // Where a linked file ends up: where it is if already in the folder, an identical file
        // already there or already planned, or a new copy under a free name.
        (string Path, long Size) Target(string full)
        {
            var size = new FileInfo(full).Length;
            if (IsUnder(full, folder)) { inFolder.Add(full); return (full, size); }
            if (existing[size].FirstOrDefault(e => hashes.Same(e, full)) is { } same) { inFolder.Add(same); return (same, size); }
            var twin = copies.FindIndex(c => c.Size == size && hashes.Same(c.From, full));
            if (twin >= 0) return (copies[twin].To, size);
            var to = Path.Combine(folder, FreeName(Path.GetFileName(full), names));
            copies.Add((full, to, size));
            return (to, size);
        }
    }

    /// <summary>
    /// Finds missing files by name anywhere under <paramref name="searchFolder"/> and points their
    /// links at them. For undo, use <see cref="PlanRelink"/> and snapshot its owners first.
    /// Returns how many links were fixed.
    /// </summary>
    public static int Relink(FamilyTree tree, string? treeFilePath, string searchFolder) =>
        PlanRelink(tree, treeFilePath, searchFolder).Apply();

    /// <summary>
    /// Looks for every missing file under <paramref name="searchFolder"/> without changing anything.
    /// A candidate must have the same name, and the same size when the link knows it. Among several,
    /// the one whose folders best match the old path wins; a tie between different files is left alone.
    /// </summary>
    public static MediaRelinkPlan PlanRelink(FamilyTree tree, string? treeFilePath, string searchFolder)
    {
        var missing = FindMissing(tree, treeFilePath);
        var wanted = new HashSet<string>(missing.Select(m => NameOf(m.Link.File)), PathComparer);
        var found = new Dictionary<string, List<FileInfo>>(PathComparer);
        if (wanted.Count > 0 && Directory.Exists(searchFolder))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var f in new DirectoryInfo(searchFolder).EnumerateFiles("*", options))
            {
                if (!wanted.Contains(f.Name)) continue;
                if (!found.TryGetValue(f.Name, out var list)) found[f.Name] = list = new();
                list.Add(f);
            }
        }
        var fixes = new List<(MediaLink Link, string File, long Size)>();
        var stillMissing = new List<MediaRef>();
        var owners = new Owners();
        foreach (var m in missing)
        {
            if (found.TryGetValue(NameOf(m.Link.File), out var candidates) && Best(candidates, m.Link) is { } best)
            {
                fixes.Add((m.Link, ToLink(treeFilePath, best.FullName), best.Length));
                owners.Add(m.Owner);
            }
            else stillMissing.Add(m);
        }
        return new MediaRelinkPlan(fixes, owners.List, stillMissing);
    }

    /// <summary>
    /// Keeps relative links pointing at the same files when the tree file moves to a new folder
    /// (Save As, or saving an imported GEDCOM as a .lineage file). Returns the records changed.
    /// </summary>
    public static IReadOnlyList<Entity> Rebase(FamilyTree tree, string? oldTreeFilePath, string newTreeFilePath)
    {
        var owners = new Owners();
        foreach (var r in AllLinks(tree))
        {
            var file = r.Link.File;
            if (file.Trim().Length == 0 || IsRemote(file) || Path.IsPathFullyQualified(file.Trim())) continue;
            var resolved = Resolve(oldTreeFilePath, file);
            if (!Path.IsPathFullyQualified(resolved)) continue;   // no old folder to be relative to
            var link = ToLink(newTreeFilePath, resolved);
            if (link == file) continue;
            r.Link.File = link;
            owners.Add(r.Owner);
        }
        return owners.List;
    }

    private static string NameOf(string file)
    {
        var f = file.Trim().Replace('\\', '/');
        return f[(f.LastIndexOf('/') + 1)..];
    }

    private static string[] Segments(string path) => path.Split('/', '\\').Where(s => s.Length > 0).ToArray();

    private static FileInfo? Best(List<FileInfo> candidates, MediaLink link)
    {
        var pool = link.Size > 0 ? candidates.Where(c => c.Length == link.Size).ToList() : candidates;
        if (pool.Count == 0) return null;   // same name, different size: a different file
        var old = Segments(link.File);
        var scored = pool.Select(c => (File: c, Score: CommonTail(old, Segments(c.FullName)))).ToList();
        var top = scored.Max(s => s.Score);
        var best = scored.Where(s => s.Score == top).Select(s => s.File).OrderBy(f => f.FullName, StringComparer.Ordinal).ToList();
        return best.All(f => f.Length == best[0].Length) ? best[0] : null;
    }

    /// <summary>How many path parts match, counting back from the file name.</summary>
    private static int CommonTail(string[] a, string[] b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && string.Equals(a[^(n + 1)], b[^(n + 1)], StringComparison.OrdinalIgnoreCase)) n++;
        return n;
    }

    private static string FreeName(string name, HashSet<string> taken)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = name;
        for (var n = 2; !taken.Add(candidate); n++) candidate = $"{stem} ({n}){ext}";
        return candidate;
    }

    /// <summary>SHA-256 of file contents, computed only when two files have the same size.</summary>
    private sealed class ContentHashes
    {
        private readonly Dictionary<string, byte[]> _cache = new(PathComparer);

        public bool Same(string a, string b) => PathComparer.Equals(a, b) || Of(a).AsSpan().SequenceEqual(Of(b));

        private byte[] Of(string path)
        {
            if (_cache.TryGetValue(path, out var hash)) return hash;
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();   // (HashData(Stream) isn't in .NET 6, which the Windows 7 build uses)
            return _cache[path] = sha.ComputeHash(stream);
        }
    }

    /// <summary>Distinct records in the order first seen.</summary>
    private sealed class Owners
    {
        private readonly HashSet<Entity> _seen = new(ReferenceEqualityComparer.Instance);
        public List<Entity> List { get; } = new();
        public void Add(Entity e) { if (_seen.Add(e)) List.Add(e); }
    }
}

/// <summary>
/// What <see cref="MediaLibrary.CopyIntoTreeFolder"/> will do, worked out without changing anything.
/// Snapshot <see cref="Owners"/> for undo, then call <see cref="Apply"/> — or <see cref="CopyFiles"/>
/// on a background thread followed by <see cref="UpdateLinks"/> on the UI thread. Apply it straight
/// away: it holds the link objects that were in the tree when it was made.
/// </summary>
public sealed class MediaCopyPlan
{
    private readonly List<(string From, string To)> _copies;
    private readonly List<(MediaLink Link, string File, long Size)> _rewrites;
    private bool _copied;

    internal MediaCopyPlan(string folder, List<(string From, string To)> copies,
                           List<(MediaLink Link, string File, long Size)> rewrites,
                           List<Entity> owners, List<string> missing, int alreadyThere)
    {
        Folder = folder;
        _copies = copies;
        _rewrites = rewrites;
        Owners = owners;
        Missing = missing;
        AlreadyThere = alreadyThere;
    }

    /// <summary>Full path of the media folder (created when the first file is copied).</summary>
    public string Folder { get; }
    /// <summary>Records whose links will change.</summary>
    public IReadOnlyList<Entity> Owners { get; }
    /// <summary>Linked files that don't exist, as written in the links.</summary>
    public IReadOnlyList<string> Missing { get; }
    public int AlreadyThere { get; }
    public int FilesToCopy => _copies.Count;
    public int LinksToUpdate => _rewrites.Count;

    /// <summary>Copies the files (doesn't touch the tree, so it can run off the UI thread). Returns how many were copied.</summary>
    /// <param name="progress">Percent of files copied, 0–100.</param>
    public int CopyFiles(IProgress<int>? progress = null)
    {
        if (_copied) return 0;
        if (_copies.Count > 0) Directory.CreateDirectory(Folder);
        for (var i = 0; i < _copies.Count; i++)
        {
            File.Copy(_copies[i].From, _copies[i].To);
            progress?.Report((i + 1) * 100 / _copies.Count);
        }
        if (_copies.Count == 0) progress?.Report(100);
        _copied = true;
        return _copies.Count;
    }

    /// <summary>Points the links at the copies. Call after <see cref="CopyFiles"/>. Returns how many links changed.</summary>
    public int UpdateLinks()
    {
        foreach (var (link, file, size) in _rewrites)
        {
            link.File = file;
            if (link.Size == 0) link.Size = size;
        }
        return _rewrites.Count;
    }

    /// <summary>Copies the files, then updates the links.</summary>
    public MediaCopyResult Apply(IProgress<int>? progress = null)
    {
        var copied = CopyFiles(progress);
        return new MediaCopyResult(copied, AlreadyThere, UpdateLinks(), Missing, Owners);
    }
}

/// <summary>Missing files found again by <see cref="MediaLibrary.PlanRelink"/>. Snapshot <see cref="Owners"/>, then <see cref="Apply"/>.</summary>
public sealed class MediaRelinkPlan
{
    private readonly List<(MediaLink Link, string File, long Size)> _fixes;

    internal MediaRelinkPlan(List<(MediaLink Link, string File, long Size)> fixes, List<Entity> owners, List<MediaRef> stillMissing)
    {
        _fixes = fixes;
        Owners = owners;
        StillMissing = stillMissing;
    }

    /// <summary>Records whose links will change.</summary>
    public IReadOnlyList<Entity> Owners { get; }
    /// <summary>Missing links with no confident match under the search folder.</summary>
    public IReadOnlyList<MediaRef> StillMissing { get; }
    public int Found => _fixes.Count;

    /// <summary>Points the links at the files found. Returns how many were fixed.</summary>
    public int Apply()
    {
        foreach (var (link, file, size) in _fixes)
        {
            link.File = file;
            link.Size = size;
        }
        return _fixes.Count;
    }
}
