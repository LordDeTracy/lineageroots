using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Avalonia.Headless.XUnit;
using Lineage.App.Services;
using Lineage.App.ViewModels;
using Lineage.Core.FamilySearch;

namespace Lineage.App.Tests;

/// <summary>
/// Plays FamilySearch's part of the sign-in: the "browser" follows the authorize URL and is sent
/// back to the app's local listener with a code, and the token endpoint checks the PKCE proof.
/// </summary>
public class FamilySearchSignInTests
{
    private sealed class FakeTokenEndpoint : HttpMessageHandler
    {
        public string? Challenge { get; set; }
        public Dictionary<string, string> Form { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            foreach (var pair in body.Split('&').Select(p => p.Split('=')))
                Form[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            var proof = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(Form["code_verifier"])))
                               .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var ok = proof == Challenge && Form["code"] == "one-time-code";
            return new HttpResponseMessage(ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = new StringContent(ok ? """{"access_token":"token-123","token_type":"bearer"}""" : """{"error":"invalid_grant"}"""),
            };
        }
    }

    [AvaloniaFact]
    public async Task SignsInWithPkceThroughTheLocalListener()
    {
        var token = new FakeTokenEndpoint();
        var api = new FamilySearchApi(integration: true, new HttpClient(token));
        var settings = new AppSettings { FamilySearchAppKey = "TEST-KEY", FamilySearchPort = 52899 };
        string? authorizeUrl = null;
        var browser = Task.CompletedTask;
        var previous = MainViewModel.Launcher;
        MainViewModel.Launcher = url =>
        {
            authorizeUrl = url;
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            token.Challenge = q["code_challenge"];
            // FamilySearch redirects the browser back to the app with a code.
            browser = Task.Run(async () =>
            {
                using var http = new HttpClient();
                var reply = await http.GetStringAsync($"{q["redirect_uri"]}?code=one-time-code&state={q["state"]}");
                Assert.Contains("signed in", reply);
            });
        };
        try
        {
            await FamilySearchSignIn.SignInAsync(api, settings, CancellationToken.None);
            await browser;
        }
        finally { MainViewModel.Launcher = previous; }

        Assert.StartsWith("https://identint.familysearch.org/cis-web/oauth2/v3/authorization?response_type=code&client_id=TEST-KEY", authorizeUrl);
        Assert.Contains("code_challenge_method=S256", authorizeUrl);
        Assert.Equal("token-123", api.AccessToken);
        Assert.Equal("http://127.0.0.1:52899/familysearch/", token.Form["redirect_uri"]);
        Assert.Equal("TEST-KEY", token.Form["client_id"]);
    }

    [AvaloniaFact]
    public async Task RejectsARedirectFromSomeoneElse()
    {
        var api = new FamilySearchApi(integration: true, new HttpClient(new FakeTokenEndpoint()));
        var settings = new AppSettings { FamilySearchAppKey = "TEST-KEY", FamilySearchPort = 52898 };
        var browser = Task.CompletedTask;
        var previous = MainViewModel.Launcher;
        MainViewModel.Launcher = url =>
        {
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            browser = Task.Run(async () =>
            {
                using var http = new HttpClient();
                await http.GetStringAsync($"{q["redirect_uri"]}?code=stolen&state=wrong-state");
            });
        };
        try
        {
            var ex = await Assert.ThrowsAsync<FamilySearchException>(() => FamilySearchSignIn.SignInAsync(api, settings, CancellationToken.None));
            Assert.Contains("didn't match", ex.Message);
            await browser;
        }
        finally { MainViewModel.Launcher = previous; }
        Assert.Null(api.AccessToken);
    }
}
