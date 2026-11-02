using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Sharing;

namespace Lineage.App.ViewModels;

/// <summary>A device asking to see the shared tree, with the host's decision.</summary>
public sealed partial class ShareDeviceRow : ObservableObject
{
    public ShareDeviceRow(ShareDevice device) => Device = device;
    public ShareDevice Device { get; }
    public string Name => Device.Name;
    public string Address => Device.Address;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsWaiting), nameof(State))] private bool? _allowed;
    public bool IsWaiting => Allowed == null;
    public string State => Allowed switch { true => "Allowed", false => "Refused", _ => "Asking to join" };
}

/// <summary>
/// Sharing on the local network (a church genealogy night): host this tree with a PIN and let each device in, or
/// find a tree someone else is sharing and get a copy of it.
/// </summary>
public sealed partial class LocalShareViewModel : ObservableObject, IDisposable
{
    private readonly Func<LocalShareHost> _makeHost;
    private readonly Func<SharedTree, string, Task> _received;
    private readonly Func<int> _beaconPort;
    private LocalShareHost? _host;
    private LocalShareFinder? _finder;
    private readonly string _deviceId = Guid.NewGuid().ToString("N");

    /// <param name="makeHost">Builds the host for the open tree (privacy-filtered copy, website for phones).</param>
    /// <param name="received">What to do with a tree that was handed over (the GEDCOM text).</param>
    public LocalShareViewModel(string treeName, Func<LocalShareHost> makeHost, Func<SharedTree, string, Task> received, Func<int>? beaconPort = null)
    {
        TreeName = treeName;
        _makeHost = makeHost;
        _received = received;
        _beaconPort = beaconPort ?? (() => LocalShareHost.DefaultBeaconPort);
        _yourName = Environment.UserName;
    }

    public string TreeName { get; }
    public bool HasTree => TreeName.Length > 0;
    public ObservableCollection<ShareDeviceRow> Devices { get; } = new();
    public ObservableCollection<SharedTree> Found { get; } = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsHosting))] private string _pin = "";
    [ObservableProperty] private string _phoneAddress = "";
    [ObservableProperty] private string _hostStatus = "";
    [ObservableProperty] private SharedTree? _selectedTree;
    [ObservableProperty] private string _joinPin = "";
    [ObservableProperty] private string _yourName;
    [ObservableProperty] private string _joinStatus = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSearching))] private bool _searching;

    public bool IsHosting => Pin.Length > 0;
    public bool IsSearching => Searching;

    // ---- hosting ----

    [RelayCommand]
    private void StartSharing()
    {
        if (_host != null || !HasTree) return;
        try
        {
            _host = _makeHost();
            _host.DeviceAsked += d => Dispatcher.UIThread.Post(() =>
            {
                Devices.Add(new ShareDeviceRow(d));
                HostStatus = $"{d.Name} is asking to join.";
            });
            _host.Start();
            Pin = _host.Pin;
            var addresses = LocalShareHost.LocalAddresses();
            PhoneAddress = addresses.Count > 0 ? string.Join("  or  ", addresses.Select(a => $"http://{a}:{_host.Port}")) : $"Port {_host.Port}";
            HostStatus = "Sharing. Others on this network will see it under Join; phones can open the address below.";
        }
        catch (Exception ex)
        {
            _host?.Dispose();
            _host = null;
            HostStatus = $"Couldn't start sharing: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StopSharing()
    {
        _host?.Dispose();
        _host = null;
        Pin = PhoneAddress = "";
        Devices.Clear();
        HostStatus = "Stopped sharing.";
    }

    [RelayCommand]
    private void Allow(ShareDeviceRow row) => Decide(row, true);

    [RelayCommand]
    private void Refuse(ShareDeviceRow row) => Decide(row, false);

    private void Decide(ShareDeviceRow row, bool allowed)
    {
        _host?.Allow(row.Device.Id, allowed);
        row.Allowed = allowed;
        HostStatus = allowed ? $"{row.Name} can now see the tree." : $"{row.Name} was refused.";
    }

    // ---- joining ----

    [RelayCommand]
    private void FindTrees()
    {
        if (_finder != null) return;
        try
        {
            _finder = new LocalShareFinder(_beaconPort());
            _finder.Found += t => Dispatcher.UIThread.Post(() =>
            {
                if (t.Id == _host?.Id) return;   // not our own
                Found.Add(t);
                SelectedTree ??= t;
                JoinStatus = "";
            });
            Searching = true;
            JoinStatus = "Looking for trees shared on this network…";
        }
        catch (Exception ex) { JoinStatus = $"Couldn't listen on the network: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task GetTreeAsync()
    {
        if (SelectedTree is not { } tree) { JoinStatus = "Pick a tree first."; return; }
        if (JoinPin.Trim().Length == 0) { JoinStatus = "Type the PIN the host gave you."; return; }
        JoinStatus = "Asking the host…";
        var result = await LocalShareClient.FetchTreeAsync(tree.BaseUri, JoinPin, _deviceId, YourName.Trim().Length > 0 ? YourName.Trim() : "A laptop");
        switch (result.Status)
        {
            case FetchStatus.Ok:
                JoinStatus = $"Got {tree.Tree}.";
                await _received(tree, result.Gedcom);
                break;
            case FetchStatus.WrongPin: JoinStatus = "That PIN isn't right."; break;
            case FetchStatus.Waiting: JoinStatus = "The host has been asked to let you in. Click Get this tree again once they have."; break;
            case FetchStatus.Refused: JoinStatus = "The host didn't let you in."; break;
            default: JoinStatus = "Couldn't reach that computer. Is it still sharing, on the same network?"; break;
        }
    }

    public void Dispose()
    {
        _host?.Dispose();
        _finder?.Dispose();
        _host = null;
        _finder = null;
    }
}
