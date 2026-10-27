using System.Formats.Tar;
using System.IO.Compression;
using Avalonia.Headless.XUnit;
using Lineage.App.Services;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class UpdaterTests
{
    private const string WinHash = "4b79526932fac4d483c3e235f6de52918b336a8a22cf085a89731d295c989b30";

    private static string Release(string tag = "v0.3.0", bool prerelease = false, bool withChecksums = true) => $$"""
        {
          "tag_name": "{{tag}}", "draft": false, "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/LordDeTracy/lineageroots/releases/tag/{{tag}}",
          "body": "New things:\n- **Faster charts**\n- Fixes\n{{(withChecksums ? $"```\\n{WinHash}  LineageRoots-0.3.0-win-x64.zip\\n```" : "")}}",
          "assets": [
            { "name": "LineageRoots-0.3.0-win-x64.zip", "browser_download_url": "https://example.test/win.zip" },
            { "name": "LineageRoots-0.3.0-linux-x64.tar.gz", "browser_download_url": "https://example.test/linux.tar.gz" },
            { "name": "LineageRoots-0.3.0-osx-x64.zip", "browser_download_url": "https://example.test/mac.zip", "digest": "sha256:{{new string('b', 64)}}" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.test/SHA256SUMS.txt" }
          ]
        }
        """;

    [Fact]
    public void ANewerReleaseIsFoundWithItsDownloadAndChecksum()
    {
        var info = Updater.ParseRelease(Release(), new Version(0, 2, 0), "win-x64.zip")!;
        Assert.Equal(new Version(0, 3, 0), info.Version);
        Assert.Equal(("v0.3.0", "LineageRoots-0.3.0-win-x64.zip", "https://example.test/win.zip", WinHash),
                     (info.Tag, info.AssetName, info.AssetUrl, info.Sha256));
        Assert.Contains("Faster charts", info.Notes);

        var linux = Updater.ParseRelease(Release(), new Version(0, 2, 0), "linux-x64.tar.gz")!;
        Assert.Equal("https://example.test/linux.tar.gz", linux.AssetUrl);
        Assert.Null(linux.Sha256);   // not in the notes: SHA256SUMS.txt is fetched instead

        Assert.Null(Updater.ParseRelease(Release(), new Version(0, 3, 0), "win-x64.zip"));          // already up to date
        Assert.Null(Updater.ParseRelease(Release("v0.1.0"), new Version(0, 2, 0), "win-x64.zip"));  // older
        Assert.Null(Updater.ParseRelease(Release(prerelease: true), new Version(0, 2, 0), "win-x64.zip"));
        Assert.Null(Updater.ParseRelease(Release(), new Version(0, 2, 0), "osx-arm64.zip"));        // nothing for this computer
        // GitHub's own record of each file's checksum is used when there is one.
        Assert.Equal(new string('b', 64), Updater.ParseRelease(Release(), new Version(0, 2, 0), "osx-x64.zip")!.Sha256);
        // A tag like "v0.2" is version 0.2.0.
        Assert.Null(Updater.ParseRelease(Release("v0.2"), new Version(0, 2, 0), "win-x64.zip"));
    }

    [Fact]
    public void VersionsAndChecksumsAreRead()
    {
        Assert.Equal(new Version(1, 2, 0), Updater.ParseVersion("v1.2"));
        Assert.Equal(new Version(0, 10, 3), Updater.ParseVersion("0.10.3-beta"));
        Assert.Null(Updater.ParseVersion("latest"));
        var sums = $"{WinHash}  LineageRoots-0.3.0-win-x64.zip\n{new string('a', 64)} *LineageRoots-0.3.0-linux-x64.tar.gz\n";
        Assert.Equal(WinHash, Updater.ChecksumFor(sums, "LineageRoots-0.3.0-win-x64.zip"));
        Assert.Equal(new string('a', 64), Updater.ChecksumFor(sums, "LineageRoots-0.3.0-linux-x64.tar.gz"));
        Assert.Null(Updater.ChecksumFor(sums, "other.zip"));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lineage-updater-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void InstallSwapsTheProgramFileFromAZipOrTarball()
    {
        var dir = TempDir();
        try
        {
            var program = Path.Combine(dir, "LineageRoots.exe");
            File.WriteAllText(program, "old version");

            // A release zip has the program inside a folder.
            var staging = Path.Combine(dir, "staging", "LineageRoots-0.3.0-win-x64");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "LineageRoots.exe"), "new version");
            File.WriteAllText(Path.Combine(staging, "README.txt"), "readme");
            var zip = Path.Combine(dir, "update.zip");
            ZipFile.CreateFromDirectory(Path.Combine(dir, "staging"), zip);

            Updater.Install(zip, program);
            Assert.Equal("new version", File.ReadAllText(program));
            Assert.Equal("old version", File.ReadAllText(program + ".old"));
            Assert.False(File.Exists(Path.Combine(dir, "README.txt")));   // only the program is replaced

            // Again from a tarball (the Linux package), over the top of the previous update.
            File.WriteAllText(Path.Combine(staging, "LineageRoots.exe"), "newer version");
            var tar = Path.Combine(dir, "update.tar.gz");
            using (var gz = new GZipStream(File.Create(tar), CompressionLevel.Fastest))
                TarFile.CreateFromDirectory(Path.Combine(dir, "staging"), gz, includeBaseDirectory: false);
            Updater.Install(tar, program);
            Assert.Equal("newer version", File.ReadAllText(program));
            Assert.Equal("new version", File.ReadAllText(program + ".old"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ABadDownloadLeavesTheProgramAlone()
    {
        var dir = TempDir();
        try
        {
            var program = Path.Combine(dir, "LineageRoots.exe");
            File.WriteAllText(program, "old version");
            Directory.CreateDirectory(Path.Combine(dir, "staging"));
            File.WriteAllText(Path.Combine(dir, "staging", "something else.txt"), "x");
            var zip = Path.Combine(dir, "update.zip");
            ZipFile.CreateFromDirectory(Path.Combine(dir, "staging"), zip);

            Assert.Throws<InvalidDataException>(() => Updater.Install(zip, program));
            Assert.Equal("old version", File.ReadAllText(program));
            Assert.False(File.Exists(program + ".old"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ADownloadThatDoesntMatchItsChecksumIsRefused()
    {
        var info = new UpdateInfo(new Version(9, 0, 0), "v9.0.0", "", "", "x.zip", "https://example.test/x.zip", Sha256: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Updater.DownloadAsync(info));
        Assert.Contains("checksum", ex.Message);
    }

    [AvaloniaFact]
    public async Task UpdateNowDownloadsInstallsAndRestarts()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var update = Updater.ParseRelease(Release(), new Version(0, 2, 0), "win-x64.zip")!;
        var installed = new List<string>();
        var restarted = new List<string>();
        var saved = (MainViewModel.CheckForUpdate, MainViewModel.DownloadUpdate, MainViewModel.InstallUpdate,
                     MainViewModel.CloseAllWindows, MainViewModel.RestartProgram, MainViewModel.CanUpdateItself, MainViewModel.ProgramPath);
        try
        {
            MainViewModel.CanUpdateItself = () => true;
            MainViewModel.ProgramPath = () => @"C:\Apps\LineageRoots.exe";
            MainViewModel.CheckForUpdate = _ => Task.FromResult<UpdateInfo?>(update);
            MainViewModel.DownloadUpdate = (u, p) => { p.Report(1); return Task.FromResult(Path.Combine(Path.GetTempPath(), "fake-download.zip")); };
            MainViewModel.InstallUpdate = (archive, program) => { installed.Add(program); return program; };
            MainViewModel.CloseAllWindows = () => Task.FromResult(true);
            MainViewModel.RestartProgram = restarted.Add;

            dialogs.ChooseAnswer = 2;   // not now
            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            Assert.Contains("Lineage Roots 0.3.0 is available", dialogs.Choices[^1]);
            Assert.Contains("• Faster charts", dialogs.Choices[^1]);
            Assert.Empty(installed);

            dialogs.ChooseAnswer = 0;   // update now
            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            Assert.Equal(new[] { @"C:\Apps\LineageRoots.exe" }, installed);
            Assert.Equal(new[] { @"C:\Apps\LineageRoots.exe" }, restarted);

            // Keeping a window open (e.g. cancelling the save question) installs for next time instead of restarting.
            MainViewModel.CloseAllWindows = () => Task.FromResult(false);
            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            Assert.Single(restarted);
            Assert.Contains("will start the next time", dialogs.Messages[^1]);

            // Nothing newer.
            MainViewModel.CheckForUpdate = _ => Task.FromResult<UpdateInfo?>(null);
            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            Assert.Contains("is the latest", dialogs.Messages[^1]);
        }
        finally
        {
            (MainViewModel.CheckForUpdate, MainViewModel.DownloadUpdate, MainViewModel.InstallUpdate,
             MainViewModel.CloseAllWindows, MainViewModel.RestartProgram, MainViewModel.CanUpdateItself, MainViewModel.ProgramPath) = saved;
        }
    }

    [AvaloniaFact]
    public async Task ATestOrDevelopmentBuildPointsToTheReleasesPage()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        Assert.False(Updater.CanUpdateItself);   // tests don't run the single-file program
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Contains("can't update itself", dialogs.Messages[^1]);
    }
}
