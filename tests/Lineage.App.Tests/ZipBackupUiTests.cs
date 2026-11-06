using Avalonia.Headless.XUnit;

namespace Lineage.App.Tests;

public class ZipBackupUiTests
{
    [AvaloniaFact]
    public async Task TheOpenTreeIsBackedUp()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var folder = Directory.CreateTempSubdirectory("lineage-zipui-").FullName;
        try
        {
            var zip = Path.Combine(folder, "backup.zip");
            dialogs.SavePath = zip;                                   // an imported tree is already saved as a family file
            await vm.BackupToZipCommand.ExecuteAsync(null);
            Assert.True(File.Exists(zip));
            Assert.StartsWith("Backed up", vm.Status);
        }
        finally { Directory.Delete(folder, true); }
    }
}
