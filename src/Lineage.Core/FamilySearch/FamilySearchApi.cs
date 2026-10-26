using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Lineage.Core.FamilySearch;

public sealed class FamilySearchException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
    public bool NeedsSignIn => Status is HttpStatusCode.Unauthorized;
}

/// <summary>
/// A small client for the FamilySearch Family Tree API. It only reads; nothing is written to
/// FamilySearch. Every app needs its own app key from FamilySearch's developer program, and the
/// user signs in on FamilySearch's own website (OAuth), so Lineage Roots never sees their password.
/// </summary>
public sealed class FamilySearchApi
{
    private readonly HttpClient _http;

    public FamilySearchApi(bool integration, HttpClient? http = null)
    {
        Integration = integration;
        _http = http ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>FamilySearch's test environment (for app keys that aren't approved for production yet).</summary>
    public bool Integration { get; }
    public string? AccessToken { get; set; }
    public bool SignedIn => AccessToken != null;

    public string ApiBase => Integration ? "https://api-integ.familysearch.org" : "https://api.familysearch.org";
    public string AuthorizeUrl => Integration
        ? "https://identint.familysearch.org/cis-web/oauth2/v3/authorization"
        : "https://ident.familysearch.org/cis-web/oauth2/v3/authorization";
    public string TokenUrl => Integration
        ? "https://identint.familysearch.org/cis-web/oauth2/v3/token"
        : "https://ident.familysearch.org/cis-web/oauth2/v3/token";

    public static string PersonPageUrl(string id) => $"https://www.familysearch.org/tree/person/details/{Uri.EscapeDataString(id)}";

    /// <summary>Exchanges the code from the sign-in redirect for an access token (PKCE, no client secret).</summary>
    public async Task SignInWithCodeAsync(string appKey, string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = appKey,
            ["redirect_uri"] = redirectUri, ["code_verifier"] = codeVerifier,
        });
        using var response = await _http.PostAsync(TokenUrl, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new FamilySearchException($"FamilySearch refused the sign-in ({(int)response.StatusCode}): {Brief(body)}", response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        AccessToken = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (AccessToken == null) throw new FamilySearchException("FamilySearch did not return an access token.");
    }

    public async Task<FsPerson?> PersonAsync(string id, CancellationToken ct = default)
    {
        using var doc = await GetAsync($"/platform/tree/persons/{Uri.EscapeDataString(id)}", "application/x-gedcomx-v1+json", ct);
        return GedcomX.Persons(doc.RootElement).FirstOrDefault(p => p.Id == id) ?? GedcomX.Persons(doc.RootElement).FirstOrDefault();
    }

    /// <summary>The person and their ancestors, numbered Ahnentafel style (1 = the person).</summary>
    public async Task<List<FsPerson>> AncestryAsync(string id, int generations, CancellationToken ct = default)
    {
        using var doc = await GetAsync($"/platform/tree/ancestry?person={Uri.EscapeDataString(id)}&generations={Math.Clamp(generations, 1, 8)}&personDetails=",
                                       "application/x-gedcomx-v1+json", ct);
        return GedcomX.Persons(doc.RootElement).Where(p => p.AscendancyNumber != null).ToList();
    }

    public async Task<List<FsPerson>> SearchAsync(string given, string surname, int birthYear, string birthPlace, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (given.Length > 0) q.Add("q.givenName=" + Uri.EscapeDataString(given));
        if (surname.Length > 0) q.Add("q.surname=" + Uri.EscapeDataString(surname));
        if (birthYear > 0) q.Add($"q.birthLikeDate={birthYear}");
        if (birthPlace.Length > 0) q.Add("q.birthLikePlace=" + Uri.EscapeDataString(birthPlace));
        q.Add("count=20");
        using var doc = await GetAsync("/platform/tree/search?" + string.Join("&", q), "application/x-gedcomx-atom+json", ct);
        return GedcomX.SearchResults(doc.RootElement);
    }

    private async Task<JsonDocument> GetAsync(string path, string accept, CancellationToken ct)
    {
        if (AccessToken == null) throw new FamilySearchException("Sign in to FamilySearch first.", HttpStatusCode.Unauthorized);
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            AccessToken = null;
            throw new FamilySearchException("Your FamilySearch sign-in has expired. Please sign in again.", response.StatusCode);
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return JsonDocument.Parse("{}");
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new FamilySearchException(response.StatusCode == HttpStatusCode.NotFound
                ? "FamilySearch has no person with that ID (it may have been merged or deleted)."
                : $"FamilySearch returned an error ({(int)response.StatusCode}): {Brief(body)}", response.StatusCode);
        return JsonDocument.Parse(body.Length > 0 ? body : "{}");
    }

    private static string Brief(string body) => body.Length > 300 ? body[..300] + "…" : body;
}
