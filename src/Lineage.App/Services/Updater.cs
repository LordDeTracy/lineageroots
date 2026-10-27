using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lineage.App.Services;

/// <summary>A newer release: its version, notes, the download for this computer and that file's SHA-256 checksum.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string AssetName, string AssetUrl, string? Sha256);

/// <summary>
/// Keeps the program up to date from its GitHub releases. The check asks GitHub for the latest release (no family
/// data is sent — only the request itself). An update downloads the release's file for this computer, checks it
/// against its SHA-256 checksum (the one GitHub records for each uploaded file, or failing that SHA256SUMS.txt or the
/// checksums in the release notes), and
/// swaps the program file: the running file is renamed aside (allowed even while it runs) and the new one put in
/// its place, then the program restarts. The old file is deleted on the next start.
/// Only the published single-file program updates itself; a development build never does.
/// </summary>
public static partial class Updater
{
    public const string Repository = "LordDeTracy/lineageroots";
    private static readonly string LatestUrl = $"https://api.github.com/repos/{Repository}/releases/latest";

    /// <summary>This program's version (major.minor.patch).</summary>
    public static Version Current
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>True for the published single-file program (the only kind that can replace itself).</summary>
    public static bool CanUpdateItself =>
#pragma warning disable IL3000 // an empty Location is exactly how a single-file program is recognised
        string.IsNullOrEmpty(typeof(Updater).Assembly.Location) && Environment.ProcessPath != null && AssetSuffix != null;
#pragma warning restore IL3000

    /// <summary>The end of the release file name for this computer, or null where no build is published.</summary>
    public static string? AssetSuffix =>
        RuntimeInformation.OSArchitecture != Architecture.X64 ? null
        : OperatingSystem.IsWindows() ? "win-x64.zip"
        : OperatingSystem.IsLinux() ? "linux-x64.tar.gz"
        : null;

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LineageRoots", Current.ToString()));
        return http;
    }

    // ---- checking ----

    /// <summary>The latest release if it's newer than this program and has a download for this computer; otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (AssetSuffix is not { } suffix) return null;
        using var http = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;   // no releases yet, or GitHub is busy: try another day
        var json = await response.Content.ReadAsStringAsync(ct);
        var info = ParseRelease(json, Current, suffix);
        if (info is { Sha256: null } && SumsUrl(json) is { } sumsUrl)
        {
            var sums = await http.GetStringAsync(sumsUrl, ct);
            info = info with { Sha256 = ChecksumFor(sums, info.AssetName) };
        }
        return info;
    }

    /// <summary>
    /// Reads GitHub's "latest release" answer. Returns null when it isn't newer than <paramref name="current"/>,
    /// is a draft or pre-release, or has no file ending in <paramref name="suffix"/>. The checksum is taken from
    /// the release notes when they list it; otherwise <see cref="CheckAsync"/> fetches SHA256SUMS.txt.
    /// </summary>
    public static UpdateInfo? ParseRelease(string json, Version current, string suffix)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
        var tag = Str(root, "tag_name");
        if (ParseVersion(tag) is not { } version || version <= current) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = Str(a, "name");
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var notes = Str(root, "body");
            // GitHub records a SHA-256 for every uploaded file ("digest"); otherwise look in the notes.
            var digest = Str(a, "digest");
            var sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && digest.Length == 71
                ? digest[7..].ToLowerInvariant()
                : ChecksumFor(notes, name);
            return new UpdateInfo(version, tag, notes, Str(root, "html_url"), name, Str(a, "browser_download_url"), sha);
        }
        return null;
    }

    private static string? SumsUrl(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        return assets.EnumerateArray().Where(a => Str(a, "name").Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                     .Select(a => Str(a, "browser_download_url")).FirstOrDefault(u => u.Length > 0);
    }

    /// <summary>"v0.3.0" → 0.3.0 (missing parts are 0); null if it isn't a version.</summary>
    public static Version? ParseVersion(string tag)
    {
        var m = VersionPattern().Match(tag);
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0,
                           m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    /// <summary>The SHA-256 listed for a file in checksum text ("hash  name" lines, as sha256sum writes them), or null.</summary>
    public static string? ChecksumFor(string text, string fileName)
    {
        foreach (Match m in ChecksumLine().Matches(text))
            if (m.Groups[2].Value.Trim().TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return m.Groups[1].Value.ToLowerInvariant();
        return null;
    }

    [GeneratedRegex(@"^\s*v?(\d+)(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\b([0-9a-fA-F]{64})\s+\*?(\S+)")]
    private static partial Regex ChecksumLine();

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    // ---- downloading and installing ----

    /// <summary>Downloads the update to a temporary file and checks its checksum (an update without one is refused).</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (update.Sha256 is not { } expected)
            throw new InvalidOperationException("This release doesn't publish a checksum for its download, so it can't be checked. Download it from the release page instead.");
        var path = Path.Combine(Path.GetTempPath(), $"lineage-update-{Guid.NewGuid():N}-{update.AssetName}");
        using var http = NewClient();
        using (var response = await http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(path);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        if (!Sha256Of(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidDataException("The download doesn't match the release's checksum, so it wasn't installed. Please try again later.");
        }
        return path;
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Puts the program from a downloaded release in place of <paramref name="programPath"/>: the current file is
    /// renamed to "….old" and the new one copied in. Returns the program path. Throws (leaving the current program as
    /// it was) if the archive has no program in it or the folder can't be written to.
    /// </summary>
    public static string Install(string archive, string programPath)
    {
        var name = Path.GetFileName(programPath);
        var work = Path.Combine(Path.GetTempPath(), $"lineage-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(archive, work);
            else
            {
                using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gz, work, overwriteFiles: true);
            }
            var fresh = Directory.EnumerateFiles(work, name, SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new InvalidDataException($"The download doesn't contain {name}.");
            var old = programPath + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(programPath, old);
            try
            {
                File.Copy(fresh, programPath);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(programPath, File.GetUnixFileMode(old) | UnixFileMode.UserExecute);
            }
            catch
            {
                if (File.Exists(programPath)) File.Delete(programPath);
                File.Move(old, programPath);   // put the working program back
                throw;
            }
            return programPath;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp files; the system cleans them up */ }
        }
    }

    /// <summary>Deletes the previous program file left by an update. Called at start-up.</summary>
    public static void CleanUpAfterUpdate()
    {
        if (Environment.ProcessPath is not { } path) return;
        try { if (File.Exists(path + ".old")) File.Delete(path + ".old"); } catch { /* still in use; next time */ }
    }

    /// <summary>Starts the (new) program again.</summary>
    public static void Restart(string programPath) => Process.Start(new ProcessStartInfo(programPath) { UseShellExecute = false });
}
