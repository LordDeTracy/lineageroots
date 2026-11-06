using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Export;
using Lineage.Core.Sharing;

namespace Lineage.App.ViewModels;

/// <summary>Sharing family history on the local network, for a genealogy night.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Where trees received over the network are kept; tests point this at a temporary folder.</summary>
    internal static string ReceivedFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lineage Roots", "Received");

    [RelayCommand]
    private void ShareOnNetwork() => _dialogs.ShowLocalShare(NewLocalShare());

    internal LocalShareViewModel NewLocalShare(Func<int>? beaconPort = null) =>
        new(HasTree ? TreeName : "", () => MakeShareHost(), ReceiveSharedTreeAsync, beaconPort);

    /// <summary>
    /// The host for the open tree. What it hands over is privacy-filtered like an export (living people hidden,
    /// private events left out), taken fresh on the UI thread each time it's asked, so it's always current.
    /// Phones get a family website built the same way, without photos.
    /// </summary>
    private LocalShareHost MakeShareHost()
    {
        var privacy = new PrivacyOptions();
        var site = Path.Combine(Path.GetTempPath(), $"lineage-shared-site-{Guid.NewGuid():N}");
        try
        {
            HtmlSite.Export(Privacy.Filter(_tree, privacy), site,
                new HtmlSiteOptions { Title = TreeName, HomePersonId = _tree.HomePersonId ?? _current?.Id, IncludePhotos = false,
                                   Language = Services.Translator.Language, Translate = Services.Translator.T });
        }
        catch (Exception) { site = null!; }
        return new LocalShareHost(TreeName,
            () => Dispatcher.UIThread.Invoke(() => GedcomExporter.Export(Privacy.Filter(_tree, privacy))), site);
    }

    /// <summary>
    /// A tree handed over: kept in Documents\Lineage Roots\Received, then — with a tree open — compared so the
    /// people wanted can be copied in; with none open, opened as a tree of its own.
    /// </summary>
    private async Task ReceiveSharedTreeAsync(SharedTree from, string gedcom)
    {
        Directory.CreateDirectory(ReceivedFolder);
        var name = SafeName($"{from.Tree} from {from.Host}");
        var path = Path.Combine(ReceivedFolder, name + ".ged");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(ReceivedFolder, $"{name} ({n}).ged");
        await File.WriteAllTextAsync(path, gedcom);
        if (HasTree) await CompareWithFileAsync(path);
        else await OpenPathAsync(path);
    }
}
