using System.IO.Compression;
using Lineage.Core.Model;
using Lineage.Core.Storage;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class ZipBackupTests
{
    [Fact]
    public void TheTreeAndItsPhotosGoInOneZip()
    {
        var folder = Directory.CreateTempSubdirectory("lineage-zip-").FullName;
        var elsewhere = Directory.CreateTempSubdirectory("lineage-zip-other-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "photos"));
            File.WriteAllBytes(Path.Combine(folder, "photos", "thomas.jpg"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(elsewhere, "interview.mp3"), new byte[] { 4, 5 });
            var t = Whitfields();
            t.People[1].Media.Add(new MediaLink { File = "photos/thomas.jpg" });
            t.People[1].Media.Add(new MediaLink { File = Path.Combine(elsewhere, "interview.mp3") });
            t.People[5].Media.Add(new MediaLink { File = "photos/thomas.jpg" });          // the same file twice goes in once
            t.People[6].Media.Add(new MediaLink { File = "photos/gone.jpg" });
            var treeFile = Path.Combine(folder, "Whitfields.lineage");
            LineageStore.Save(t, treeFile);

            var zipPath = Path.Combine(folder, "backup.zip");
            var result = ZipBackup.Create(treeFile, t, zipPath);
            Assert.Equal((3, 1), (result.Files, result.Missing));
            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("Whitfields.lineage", names);
            Assert.Contains("photos/thomas.jpg", names);
            Assert.Contains("Linked files/interview.mp3", names);
            Assert.Contains("About this backup.txt", names);
            Assert.False(File.Exists(zipPath + ".part"));
        }
        finally
        {
            Directory.Delete(folder, true);
            Directory.Delete(elsewhere, true);
        }
    }
}
