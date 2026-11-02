using System.Net;
using Lineage.Core.Sharing;

namespace Lineage.Core.Tests;

/// <summary>Sharing on a local network, over real sockets on this computer (loopback stands in for the Wi-Fi).</summary>
public class LocalShareTests
{
    private static int FreeUdpPort() => Random.Shared.Next(40000, 49000);

    [Fact]
    public async Task ALaptopFindsTheTreeAndGetsItOnlyWithThePinAndTheHostsSayso()
    {
        var beacon = FreeUdpPort();
        using var finder = new LocalShareFinder(beacon);
        var asked = new List<ShareDevice>();
        using var host = new LocalShareHost("Gray family", () => "0 HEAD\n0 TRLR\n", null, pin: "4321",
                                            beaconTarget: new IPEndPoint(IPAddress.Loopback, beacon));
        host.DeviceAsked += asked.Add;
        host.Start();

        // Found by its announcement.
        SharedTree? found = null;
        for (var i = 0; i < 50 && found == null; i++)
        {
            found = finder.Trees.FirstOrDefault(t => t.Id == host.Id);
            if (found == null) await Task.Delay(100);
        }
        Assert.NotNull(found);
        Assert.Equal(("Gray family", host.Port), (found!.Tree, found.Port));

        var uri = new Uri($"http://127.0.0.1:{host.Port}/");
        Assert.Equal(FetchStatus.WrongPin, (await LocalShareClient.FetchTreeAsync(uri, "1111", "laptop-1", "Ann's laptop")).Status);
        Assert.Empty(asked);
        Assert.Equal(FetchStatus.Waiting, (await LocalShareClient.FetchTreeAsync(uri, "4321", "laptop-1", "Ann's laptop")).Status);
        Assert.Equal("Ann's laptop", Assert.Single(asked).Name);   // the host is asked once

        host.Allow("laptop-1", true);
        var ok = await LocalShareClient.FetchTreeAsync(uri, "4321", "laptop-1", "Ann's laptop");
        Assert.Equal((FetchStatus.Ok, "0 HEAD\n0 TRLR\n"), (ok.Status, ok.Gedcom));

        await LocalShareClient.FetchTreeAsync(uri, "4321", "laptop-2", "Someone else");
        host.Allow("laptop-2", false);
        Assert.Equal(FetchStatus.Refused, (await LocalShareClient.FetchTreeAsync(uri, "4321", "laptop-2", "Someone else")).Status);
    }

    [Fact]
    public async Task APhoneSeesTheFamilyWebsiteOnceLetIn()
    {
        var site = Path.Combine(Path.GetTempPath(), $"lineage-share-site-{Guid.NewGuid():N}");
        Directory.CreateDirectory(site);
        await File.WriteAllTextAsync(Path.Combine(site, "index.html"), "<h1>The Gray family</h1>");
        try
        {
            using var host = new LocalShareHost("Gray family", () => "", site, pin: "2468",
                                                beaconTarget: new IPEndPoint(IPAddress.Loopback, FreeUdpPort()));
            host.Start();
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            using var phone = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{host.Port}/") };

            Assert.Contains("The host's PIN", await phone.GetStringAsync("/"));
            Assert.Contains("isn't right", await phone.GetStringAsync("/?name=Ann&pin=0000"));
            Assert.Contains("Waiting to be let in", await phone.GetStringAsync("/?name=Ann&pin=2468"));
            var device = Assert.Single(host.Devices);
            Assert.Equal("Ann", device.Name);

            host.Allow(device.Id, true);
            Assert.Contains("The Gray family", await phone.GetStringAsync("/"));   // the PIN is remembered
            var outside = await phone.GetAsync("/../../secret.txt");
            Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);              // nothing outside the website
        }
        finally { Directory.Delete(site, recursive: true); }
    }
}
