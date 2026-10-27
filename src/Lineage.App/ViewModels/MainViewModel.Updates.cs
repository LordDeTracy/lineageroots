using CommunityToolkit.Mvvm.Input;
using Lineage.App.Services;

namespace Lineage.App.ViewModels;

/// <summary>Updating the program from its GitHub releases (see <see cref="Updater"/>).</summary>
public sealed partial class MainViewModel
{
    private static int _checkedOnStart;

    /// <summary>The network side of updating; tests replace it so nothing is downloaded.</summary>
    internal static Func<CancellationToken, Task<UpdateInfo?>> CheckForUpdate { get; set; } = Updater.CheckAsync;
    internal static Func<UpdateInfo, IProgress<double>, Task<string>> DownloadUpdate { get; set; } =
        (u, p) => Updater.DownloadAsync(u, p);
    internal static Func<string, string, string> InstallUpdate { get; set; } = Updater.Install;
    internal static Func<Task<bool>> CloseAllWindows { get; set; } = App.CloseAllWindowsAsync;
    internal static Action<string> RestartProgram { get; set; } = Updater.Restart;
    internal static Func<bool> CanUpdateItself { get; set; } = () => Updater.CanUpdateItself;
    internal static Func<string?> ProgramPath { get; set; } = () => Environment.ProcessPath;

    /// <summary>Once per run, a few seconds after the first window opens: offers a newer version if there is one.</summary>
    public async Task CheckForUpdatesOnStartAsync()
    {
        if (!CanUpdateItself() || !_settings.CheckForUpdates || Interlocked.Exchange(ref _checkedOnStart, 1) == 1) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var update = await CheckForUpdate(timeout.Token);
            if (update == null || update.Tag == _settings.SkippedVersion) return;
            await OfferUpdateAsync(update);
        }
        catch (Exception)
        {
            // Offline, GitHub unreachable or busy: never a reason to bother anyone at start-up.
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (!CanUpdateItself())
        {
            await _dialogs.ShowMessageAsync("Check for updates",
                $"You have version {Updater.Current}. This copy can't update itself (it isn't the downloaded program), " +
                $"so get new versions from https://github.com/{Updater.Repository}/releases.");
            return;
        }
        UpdateInfo? update;
        try
        {
            Status = "Checking for updates…";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            update = await CheckForUpdate(timeout.Token);
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("Couldn't check for updates", $"GitHub couldn't be reached ({ex.Message}). Try again later.");
            return;
        }
        Status = "";
        if (update == null)
            await _dialogs.ShowMessageAsync("You're up to date", $"Version {Updater.Current} is the latest.");
        else
            await OfferUpdateAsync(update);
    }

    private async Task OfferUpdateAsync(UpdateInfo update)
    {
        var notes = string.Join("\n", update.Notes.Replace("\r", "").Split('\n')
            .Select(l => l.Trim()).Where(l => l.StartsWith("- ") || l.StartsWith("* ")).Take(6)
            .Select(l => "• " + l[2..].Replace("**", "")));
        var choice = await _dialogs.ChooseAsync($"Lineage Roots {update.Version} is available",
            $"You have version {Updater.Current}." + (notes.Length > 0 ? $" What's new:\n{notes}" : "") +
            "\n\nUpdating downloads the new version, checks it, and restarts Lineage Roots. Your trees aren't touched.",
            "Update now", "Skip this version", "Not now");
        if (choice == 1)
        {
            _settings.SkippedVersion = update.Tag;
            try { _settings.Save(); } catch (Exception) { /* only a preference */ }
            return;
        }
        if (choice != 0) return;
        await InstallAsync(update);
    }

    private async Task InstallAsync(UpdateInfo update)
    {
        if (ProgramPath() is not { } program) return;
        try
        {
            var progress = new Progress<double>(f => Status = $"Downloading Lineage Roots {update.Version}… {f:P0}");
            Status = $"Downloading Lineage Roots {update.Version}…";
            var archive = await DownloadUpdate(update, progress);
            Status = "Installing the update…";
            await Task.Run(() => InstallUpdate(archive, program));
            try { File.Delete(archive); } catch { /* temp file */ }
        }
        catch (Exception ex)
        {
            Status = "";
            await _dialogs.ShowMessageAsync("The update didn't install",
                $"{ex.Message}\n\nYour current version is unchanged. You can also download the new version from {update.PageUrl}.");
            return;
        }
        Status = "";
        // Unsaved changes are offered for saving in every window before the restart.
        if (await CloseAllWindows())
            RestartProgram(program);
        else
            await _dialogs.ShowMessageAsync("Update installed",
                $"Lineage Roots {update.Version} is installed and will start the next time you open the program.");
    }
}
