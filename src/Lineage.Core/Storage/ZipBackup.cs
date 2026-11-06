using System.IO.Compression;
using Lineage.Core.Media;
using Lineage.Core.Model;

namespace Lineage.Core.Storage;

/// <summary>
/// One zip file holding a family file and every photo, recording and document it links to, to keep somewhere safe — a
/// USB stick, or a OneDrive, Google Drive or Dropbox folder. Unzipped anywhere, the tree opens with its photos in place:
/// files beside the tree keep their folders; files from elsewhere go into a "Linked files" folder (Find missing photos
/// re-links them). The family file itself is stored exactly as saved, so a password stays on.
/// </summary>
public static class ZipBackup
{
    public sealed record Result(int Files, int Missing, long Bytes);

    public static Result Create(string treeFile, FamilyTree tree, string zipPath)
    {
        var treeFolder = Path.GetDirectoryName(Path.GetFullPath(treeFile))!;
        var missing = 0;
        var files = 0;
        var added = new HashSet<string>(MediaLibrary.PathComparer);
        var temp = zipPath + ".part";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(treeFile, Path.GetFileName(treeFile), CompressionLevel.Optimal);
            files++;
            foreach (var link in MediaLibrary.AllLinks(tree).Select(r => r.Link.File).Distinct(MediaLibrary.PathComparer))
            {
                var full = MediaLibrary.Resolve(treeFile, link);
                if (!File.Exists(full)) { missing++; continue; }
                if (!added.Add(full)) continue;
                var relative = Path.GetRelativePath(treeFolder, full);
                var inside = !relative.StartsWith("..") && !Path.IsPathRooted(relative)
                    ? relative : Path.Combine("Linked files", Path.GetFileName(full));
                zip.CreateEntryFromFile(full, inside.Replace('\\', '/'), CompressionLevel.Fastest);   // photos are already compressed
                files++;
            }
            var note = zip.CreateEntry("About this backup.txt");
            using var w = new StreamWriter(note.Open());
            w.WriteLine($"A backup of {Path.GetFileName(treeFile)} made by Lineage Roots on {DateTime.Now:d MMMM yyyy, HH:mm}.");
            w.WriteLine("Unzip it into a folder and open the family file. Photos kept beside the family file are in their own folders;");
            w.WriteLine("photos that were elsewhere on the computer are in \"Linked files\" (re-link them with Find missing photos).");
            if (missing > 0) w.WriteLine($"{missing} linked file(s) could not be found when the backup was made.");
        }
        if (File.Exists(zipPath)) File.Delete(zipPath);
        File.Move(temp, zipPath);
        return new Result(files, missing, new FileInfo(zipPath).Length);
    }
}
