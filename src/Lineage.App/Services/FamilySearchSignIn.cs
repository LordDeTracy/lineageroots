using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Lineage.Core.FamilySearch;

namespace Lineage.App.Services;

/// <summary>
/// Signs in to FamilySearch the way desktop apps should (OAuth 2 with PKCE): the user's browser
/// opens FamilySearch's own sign-in page, and FamilySearch sends a one-time code back to a
/// temporary listener on this computer. Lineage Roots never sees the password, and the access
/// token is kept in memory only.
/// </summary>
public static class FamilySearchSignIn
{
    public static async Task SignInAsync(FamilySearchApi api, AppSettings settings, CancellationToken ct)
    {
        var redirect = settings.FamilySearchRedirectUri;
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        try { listener.Start(); }
        catch (HttpListenerException ex)
        {
            throw new FamilySearchException($"Couldn't listen for the sign-in on {redirect} ({ex.Message}). Try another port in FamilySearch settings.");
        }

        var url = $"{api.AuthorizeUrl}?response_type=code&client_id={Uri.EscapeDataString(settings.FamilySearchAppKey)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirect)}&state={state}" +
                  $"&code_challenge={challenge}&code_challenge_method=S256";
        ViewModels.MainViewModel.OpenExternal(url);

        var wait = listener.GetContextAsync();
        var done = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMinutes(5), ct));
        if (done != wait) throw new FamilySearchException(ct.IsCancellationRequested ? "Sign-in cancelled." : "Sign-in timed out after 5 minutes.");
        var context = await wait;

        var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
        var ok = query["state"] == state && query["code"] is { Length: > 0 };
        await Respond(context, ok
            ? "You're signed in to FamilySearch. You can close this tab and go back to Lineage Roots."
            : "Sign-in didn't complete. You can close this tab and try again from Lineage Roots.");
        if (query["error"] is { } error) throw new FamilySearchException($"FamilySearch sign-in failed: {query["error_description"] ?? error}");
        if (!ok) throw new FamilySearchException("FamilySearch sign-in failed: the response didn't match this request.");

        await api.SignInWithCodeAsync(settings.FamilySearchAppKey, query["code"]!, redirect, verifier, ct);
    }

    private static async Task Respond(HttpListenerContext context, string message)
    {
        var html = $"<!doctype html><meta charset=utf-8><title>Lineage Roots</title>" +
                   $"<body style=\"font-family:system-ui,sans-serif;background:#F6F3EE;color:#22252A;display:grid;place-items:center;height:90vh\">" +
                   $"<p style=\"font-size:18px\">{WebUtility.HtmlEncode(message)}</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
