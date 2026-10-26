using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.App.Services;

/// <summary>A tree with unsaved changes that was auto-saved and never saved properly (e.g. after a crash).</summary>
public sealed record RecoveryEntry(string RecoveryFile, string? OriginalPath, string TreeName, DateTime SavedAt)
{
    public string Description => $"{TreeName} — unsaved changes from {SavedAt:g}";
}

/// <summary>
/// Keeps work safe without getting in the way:
/// - before a save overwrites a tree file, the previous version is copied to a backups folder
///   (the newest few are kept);
/// - while there are unsaved changes, a recovery copy is written every few minutes to a separate
///   folder — never over the user's own file — and offered back if the app closes unexpectedly.
/// Both live under %LocalAppData%\LineageRoots.
/// </summary>
public static class Backups
{
    public static string Root { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LineageRoots");

    public static string BackupFolder => Path.Combine(Root, "Backups");
    private static string RecoveryFolder => Path.Combine(Root, "Recovery");

    /// <summary>Copies the file about to be overwritten into the backups folder and trims old copies.</summary>
    public static void BackUpBeforeSave(string path, int keep)
    {
        if (keep <= 0 || !File.Exists(path)) return;
        var name = Path.GetFileNameWithoutExtension(path);
        var folder = Path.Combine(BackupFolder, Safe(name));
        Directory.CreateDirectory(folder);
        var stamp = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HHmmss");
        var target = Path.Combine(folder, $"{name} {stamp}.lineage");
        if (!File.Exists(target)) File.Copy(path, target);
        foreach (var old in new DirectoryInfo(folder).GetFiles("*.lineage").OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep))
            old.Delete();
    }

    private sealed record Info(string? OriginalPath, string TreeName, DateTime SavedAt);

    private static string Key(string? path, string treeName) => path != null
        ? "file-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..16]
        : "unsaved-" + Safe(treeName);

    /// <summary>Writes the recovery copy. Safe to call from a background thread with a tree that isn't changing.</summary>
    public static void SaveRecovery(FamilyTree tree, string? originalPath)
    {
        Directory.CreateDirectory(RecoveryFolder);
        var key = Key(originalPath, tree.Name);
        var file = Path.Combine(RecoveryFolder, key + ".lineage");
        LineageStore.Save(tree, file);
        File.WriteAllText(Path.Combine(RecoveryFolder, key + ".json"),
            JsonSerializer.Serialize(new Info(originalPath, tree.Name, DateTime.Now)));
    }

    /// <summary>Forgets the recovery copy once the work is saved (or deliberately discarded).</summary>
    public static void ClearRecovery(string? originalPath, string treeName)
    {
        var key = Key(originalPath, treeName);
        foreach (var ext in new[] { ".lineage", ".json" })
        {
            var f = Path.Combine(RecoveryFolder, key + ext);
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { /* in use; harmless */ }
        }
    }

    /// <summary>Every recovery copy still on disk, newest first.</summary>
    public static List<RecoveryEntry> Pending()
    {
        if (!Directory.Exists(RecoveryFolder)) return new();
        var list = new List<RecoveryEntry>();
        foreach (var json in Directory.GetFiles(RecoveryFolder, "*.json"))
        {
            try
            {
                var info = JsonSerializer.Deserialize<Info>(File.ReadAllText(json));
                var file = Path.ChangeExtension(json, ".lineage");
                if (info != null && File.Exists(file)) list.Add(new RecoveryEntry(file, info.OriginalPath, info.TreeName, info.SavedAt));
            }
            catch (Exception) { /* a damaged entry is skipped */ }
        }
        return list.OrderByDescending(e => e.SavedAt).ToList();
    }

    /// <summary>A recovery copy for this file that is newer than the file itself, if any.</summary>
    public static RecoveryEntry? For(string path)
    {
        var full = Path.GetFullPath(path);
        return Pending().FirstOrDefault(e => e.OriginalPath != null
            && string.Equals(Path.GetFullPath(e.OriginalPath), full, StringComparison.OrdinalIgnoreCase)
            && e.SavedAt > File.GetLastWriteTime(path));
    }

    public static void Discard(RecoveryEntry e)
    {
        foreach (var f in new[] { e.RecoveryFile, Path.ChangeExtension(e.RecoveryFile, ".json") })
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { }
    }

    private static string Safe(string name)
    {
        var s = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return s.Length == 0 ? "tree" : s.Length > 60 ? s[..60] : s;
    }
}
