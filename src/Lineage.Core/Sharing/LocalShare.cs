using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Lineage.Core.Sharing;

/// <summary>A laptop or phone that asked to see the shared tree.</summary>
public sealed class ShareDevice
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    /// <summary>Null while waiting for the host to decide.</summary>
    public bool? Allowed { get; set; }
}

/// <summary>A tree being shared on the network, as another device sees it.</summary>
public sealed record SharedTree(string Id, string Tree, string Host, string Address, int Port)
{
    public Uri BaseUri => new($"http://{Address}:{Port}/");
}

public enum FetchStatus { Ok, WrongPin, Waiting, Refused, Unreachable }

public sealed record FetchResult(FetchStatus Status, string Gedcom = "");

/// <summary>
/// Sharing family history between devices on the same network — a church genealogy night — with no internet. One
/// device hosts: a small web server on the local network, announced by a broadcast every couple of seconds so others
/// find it. Nothing is handed over without both the host's PIN and the host's say-so for each device. Laptops running
/// Lineage Roots download the (privacy-filtered) tree as GEDCOM and pick who to copy in; phones open the address in a
/// browser and see the family website.
/// </summary>
public sealed class LocalShareHost : IDisposable
{
    public const int DefaultBeaconPort = 52819;
    private const string App = "LineageRoots";

    private readonly Func<string> _gedcom;
    private readonly string? _site;
    private readonly IPEndPoint _beaconTarget;
    private readonly ConcurrentDictionary<string, ShareDevice> _devices = new();
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;

    /// <param name="gedcom">The tree to hand over, already privacy-filtered (asked for each time, so it's current).</param>
    /// <param name="siteFolder">A family website for phones and browsers, or null.</param>
    /// <param name="beaconTarget">Where announcements go: the network broadcast address, or loopback in tests.</param>
    public LocalShareHost(string tree, Func<string> gedcom, string? siteFolder, string? pin = null, IPEndPoint? beaconTarget = null)
    {
        Tree = tree;
        _gedcom = gedcom;
        _site = siteFolder;
        Pin = pin ?? Random.Shared.Next(1000, 10000).ToString();
        _beaconTarget = beaconTarget ?? new IPEndPoint(IPAddress.Broadcast, DefaultBeaconPort);
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Tree { get; }
    public string Pin { get; }
    public int Port { get; private set; }
    /// <summary>Raised (on a background thread) when a new device asks with the right PIN.</summary>
    public event Action<ShareDevice>? DeviceAsked;
    public IReadOnlyList<ShareDevice> Devices => _devices.Values.OrderBy(d => d.Name).ToList();

    /// <summary>This computer's addresses on the local network, for phones to type in.</summary>
    public static IReadOnlyList<string> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString()).Distinct().ToList();

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
        _ = Task.Run(AnnounceAsync);
    }

    public void Allow(string deviceId, bool allowed)
    {
        if (_devices.TryGetValue(deviceId, out var d)) d.Allowed = allowed;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener?.Stop(); } catch (Exception) { }
    }

    // ---- announcing ----

    private async Task AnnounceAsync()
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var beacon = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            app = App, v = 1, id = Id, tree = Tree, host = Environment.MachineName, port = Port,
        }));
        while (!_stop.IsCancellationRequested)
        {
            try { await udp.SendAsync(beacon, beacon.Length, _beaconTarget); } catch (Exception) { /* no network right now */ }
            try { await Task.Delay(2000, _stop.Token); } catch (OperationCanceledException) { break; }
        }
    }

    // ---- the web server ----

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { break; }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10000;
                var stream = client.GetStream();
                var (path, query, cookies) = await ReadRequestAsync(stream);
                if (path == null) return;
                var address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                await RespondAsync(stream, path, query, cookies, address);
            }
            catch (Exception) { /* a dropped connection */ }
        }
    }

    private static async Task<(string? Path, Dictionary<string, string> Query, Dictionary<string, string> Cookies)> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[16384];
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total));
            if (n == 0) break;
            total += n;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
        }
        var lines = Encoding.UTF8.GetString(buffer, 0, total).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2 || first[0] != "GET") return (null, new(), new());
        var target = first[1];
        var q = target.IndexOf('?');
        var path = Uri.UnescapeDataString(q >= 0 ? target[..q] : target);
        var query = Parse(q >= 0 ? target[(q + 1)..] : "", '&');
        var cookieLine = lines.FirstOrDefault(l => l.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) ?? "";
        var cookies = Parse(cookieLine.Length > 7 ? cookieLine[7..] : "", ';');
        return (path, query, cookies);
    }

    private static Dictionary<string, string> Parse(string text, char separator) =>
        text.Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim().Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1].Replace('+', ' ')));

    private async Task RespondAsync(NetworkStream stream, string path, Dictionary<string, string> query,
                                    Dictionary<string, string> cookies, string address)
    {
        if (path == "/api/hello")
        {
            await SendAsync(stream, 200, "application/json", JsonSerializer.Serialize(new { app = App, tree = Tree, id = Id }));
            return;
        }
        if (path == "/api/tree")
        {
            var (status, _) = Admit(query.GetValueOrDefault("pin"), query.GetValueOrDefault("device"), query.GetValueOrDefault("name"), address);
            if (status == FetchStatus.Ok) await SendAsync(stream, 200, "text/plain; charset=utf-8", _gedcom());
            else await SendAsync(stream, status switch { FetchStatus.WrongPin => 401, FetchStatus.Waiting => 202, _ => 403 }, "text/plain", status.ToString());
            return;
        }

        // A browser (a phone): the PIN once, then the host's say-so, then the family website.
        var device = cookies.GetValueOrDefault("lrdevice") is { Length: > 0 } d ? d : Guid.NewGuid().ToString("N");
        var pin = query.GetValueOrDefault("pin") ?? cookies.GetValueOrDefault("lrpin");
        var (admitted, deviceRecord) = Admit(pin, device, query.GetValueOrDefault("name") ?? cookies.GetValueOrDefault("lrname") ?? "A phone", address);
        var setCookies = new List<string> { $"lrdevice={device}; Path=/" };
        if (pin != null && admitted != FetchStatus.WrongPin) setCookies.Add($"lrpin={Uri.EscapeDataString(pin)}; Path=/");
        if (query.GetValueOrDefault("name") is { Length: > 0 } named) setCookies.Add($"lrname={Uri.EscapeDataString(named)}; Path=/");
        switch (admitted)
        {
            case FetchStatus.WrongPin:
                await SendAsync(stream, 200, "text/html; charset=utf-8", PinPage(pin != null), setCookies);
                return;
            case FetchStatus.Waiting:
                await SendAsync(stream, 200, "text/html; charset=utf-8",
                    Page("Waiting to be let in", $"<p>The host is being asked to let <b>{Html(deviceRecord?.Name ?? "this device")}</b> see {Html(Tree)}.</p><p>This page checks again by itself.</p>",
                         refresh: true), setCookies);
                return;
            case FetchStatus.Refused:
                await SendAsync(stream, 403, "text/html; charset=utf-8", Page("Not allowed", "<p>The host didn't let this device in.</p>"), setCookies);
                return;
        }
        if (_site == null)
        {
            await SendAsync(stream, 200, "text/html; charset=utf-8", Page(Tree, "<p>This tree is shared for Lineage Roots on other computers.</p>"), setCookies);
            return;
        }
        var relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (relative.Length == 0) relative = "index.html";
        var full = Path.GetFullPath(Path.Combine(_site, relative));
        if (!full.StartsWith(Path.GetFullPath(_site), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            await SendAsync(stream, 404, "text/plain", "Not found", setCookies);
            return;
        }
        await SendAsync(stream, 200, ContentType(full), await File.ReadAllBytesAsync(full), setCookies);
    }

    /// <summary>The PIN first, then the host's decision for this device (asked once).</summary>
    private (FetchStatus, ShareDevice?) Admit(string? pin, string? deviceId, string? name, string address)
    {
        if (pin == null || pin.Trim() != Pin || string.IsNullOrEmpty(deviceId)) return (FetchStatus.WrongPin, null);
        var isNew = false;
        var device = _devices.GetOrAdd(deviceId, id =>
        {
            isNew = true;
            return new ShareDevice { Id = id, Name = string.IsNullOrWhiteSpace(name) ? "A device" : name.Trim(), Address = address };
        });
        if (isNew) DeviceAsked?.Invoke(device);
        return (device.Allowed switch { true => FetchStatus.Ok, false => FetchStatus.Refused, _ => FetchStatus.Waiting }, device);
    }

    private string PinPage(bool wrong) => Page($"{Tree}",
        (wrong ? "<p style='color:#c0563f'>That PIN isn't right.</p>" : "") +
        "<form method='get' action='/'><p>Your name<br><input name='name' autocomplete='name' style='font-size:1.2em;width:100%'></p>" +
        "<p>The host's PIN<br><input name='pin' inputmode='numeric' style='font-size:1.6em;width:6em'></p>" +
        "<p><button style='font-size:1.2em;padding:.4em 1.2em'>Join</button></p></form>");

    private static string Page(string title, string body, bool refresh = false) =>
        "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
        (refresh ? "<meta http-equiv='refresh' content='3'>" : "") +
        $"<title>{Html(title)}</title><style>body{{font-family:system-ui,sans-serif;max-width:32em;margin:2em auto;padding:0 1em}}</style>" +
        $"</head><body><h1>{Html(title)}</h1>{body}<p style='color:#888'>Shared with Lineage Roots on this network.</p></body></html>";

    private static string Html(string s) => WebUtility.HtmlEncode(s);

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private static Task SendAsync(NetworkStream stream, int status, string type, string body, IEnumerable<string>? cookies = null) =>
        SendAsync(stream, status, type, Encoding.UTF8.GetBytes(body), cookies);

    private static async Task SendAsync(NetworkStream stream, int status, string type, byte[] body, IEnumerable<string>? cookies = null)
    {
        var reason = status switch { 200 => "OK", 202 => "Accepted", 401 => "Unauthorized", 403 => "Forbidden", _ => "Not Found" };
        var head = new StringBuilder($"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                                     "Cache-Control: no-store\r\nConnection: close\r\n");
        foreach (var c in cookies ?? Array.Empty<string>()) head.Append($"Set-Cookie: {c}\r\n");
        head.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(body);
    }
}

/// <summary>Listens for trees shared on the network.</summary>
public sealed class LocalShareFinder : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, SharedTree> _found = new();
    private readonly UdpClient _udp;

    public LocalShareFinder(int port = LocalShareHost.DefaultBeaconPort)
    {
        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _ = Task.Run(ListenAsync);
    }

    /// <summary>Raised (on a background thread) the first time a shared tree is heard.</summary>
    public event Action<SharedTree>? Found;
    public IReadOnlyList<SharedTree> Trees => _found.Values.ToList();

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await _udp.ReceiveAsync(_stop.Token); }
            catch (Exception) { break; }
            try
            {
                using var doc = JsonDocument.Parse(packet.Buffer);
                var r = doc.RootElement;
                if (r.GetProperty("app").GetString() != "LineageRoots") continue;
                var tree = new SharedTree(r.GetProperty("id").GetString()!, r.GetProperty("tree").GetString() ?? "",
                                          r.GetProperty("host").GetString() ?? "", packet.RemoteEndPoint.Address.ToString(), r.GetProperty("port").GetInt32());
                if (_found.TryAdd(tree.Id, tree)) Found?.Invoke(tree);
            }
            catch (Exception) { /* not ours */ }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
    }
}

/// <summary>Asks a host for its tree.</summary>
public static class LocalShareClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<FetchResult> FetchTreeAsync(Uri host, string pin, string deviceId, string deviceName, CancellationToken ct = default)
    {
        var url = new Uri(host, $"api/tree?pin={Uri.EscapeDataString(pin.Trim())}&device={Uri.EscapeDataString(deviceId)}&name={Uri.EscapeDataString(deviceName)}");
        try
        {
            using var response = await Http.GetAsync(url, ct);
            return (int)response.StatusCode switch
            {
                200 => new FetchResult(FetchStatus.Ok, await response.Content.ReadAsStringAsync(ct)),
                401 => new FetchResult(FetchStatus.WrongPin),
                202 => new FetchResult(FetchStatus.Waiting),
                _ => new FetchResult(FetchStatus.Refused),
            };
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return new FetchResult(FetchStatus.Unreachable); }
    }
}
